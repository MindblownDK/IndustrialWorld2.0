// Assets/Scripts/VoxelEngine/UI/RailPanels.cs
//
// The rail consoles as right-dock machine cards (12.6.0): station, switch,
// footplate, schedule and display - migrated off the retired center-modal HUDs
// (RailConfigHud, TrainScheduleHud, DisplayConfigHud) so the whole railway is
// worked from the side dock like every other machine. Steampunk dressed: brass
// badges, riveted dividers and analog dials, because the UI matches the block.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RailPanels
    {
        // ── Shared chrome ────────────────────────────────────────────────────
        private static VisualElement Header(string icon, string title, string status, Color statusColor)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.pickingMode = PickingMode.Ignore;

            row.Add(T.IconBadge(icon, LcdHudTheme.Brass));

            var titleLbl = T.Title(title);
            titleLbl.style.flexGrow = 1;
            titleLbl.style.fontSize = 15;
            row.Add(titleLbl);

            var (pill, _) = T.StatusPill(status, statusColor);
            row.Add(pill);

            return row;
        }

        private static void Refresh() => GameUIController.Instance?.RefreshCurrentPanel();

        // ── Station ──────────────────────────────────────────────────────────
        public static VisualElement StationPanel(RailStation station)
        {
            if (station == null) return T.MachinePanel();
            var p = T.MachinePanel();

            var platform = station.ServedTrack;
            bool ok = platform != null;
            p.Add(Header("🚉", "Rail Station", ok ? "ON LINE" : "NO TRACK",
                ok ? T.AccentGreen : T.AccentAmber));
            p.Add(SteampunkTheme.RivetedDivider());

            var field = new TextField("Station name")
                { value = station.HasCustomName ? station.StationName : "" };
            field.style.marginBottom = 4;
            SteampunkTheme.GuardTextField(field);
            field.RegisterValueChangedCallback(e => station.StationName = e.newValue ?? "");
            p.Add(field);
            p.Add(T.Muted("Schedules refer to this name. Renaming a station re-points every " +
                "train that already calls here."));
            p.Add(T.Spacer(4));

            p.Add(T.Subtitle("Role"));
            var roleRow = new VisualElement();
            roleRow.style.flexDirection = FlexDirection.Row;
            roleRow.style.marginBottom = 4;
            foreach (StationRole role in System.Enum.GetValues(typeof(StationRole)))
            {
                var captured = role;
                var b = SteampunkTheme.SelectorButton(captured.ToString().ToUpperInvariant(),
                    station.role == captured, () => { station.role = captured; Refresh(); });
                b.style.marginRight = 4;
                roleRow.Add(b);
            }
            p.Add(roleRow);
            p.Add(T.Muted(station.role switch
            {
                StationRole.Load => "Moves cargo from this station's hold INTO a docked train.",
                StationRole.Unload => "Moves cargo from a docked train INTO this station's hold.",
                _ => "A timing point. Trains stop here but no cargo moves.",
            }));
            p.Add(T.Spacer(4));

            p.Add(T.Subtitle("Status"));
            if (!ok)
            {
                var warn = new Label($"No rail track within {RailStation.ServiceRadius:0} m. Lay track " +
                    "beside the station so trains can reach it.");
                warn.style.fontSize = 10;
                warn.style.whiteSpace = WhiteSpace.Normal;
                warn.style.color = new StyleColor(T.AccentAmber);
                warn.style.marginBottom = 6;
                p.Add(warn);
            }
            else
            {
                int used = 0;
                for (int i = 0; i < station.Hold.Size; i++)
                    if (!station.Hold.GetSlot(i).IsEmpty) used++;
                p.Add(T.StatRow("📦", "Hold", $"{used}/{station.Hold.Size} slots used", T.TextSecondary));
                p.Add(T.StatRow("🔁", "Transfer", $"{station.transferPerSecond:0}/s", T.TextSecondary));
                p.Add(T.StatRow("⏱", "Dwell", $"{station.dwellSeconds:0}s", T.TextSecondary));
            }

            p.Add(T.Spacer(4));
            var openHold = T.SmallButton("OPEN STATION HOLD",
                () => GameUIController.Instance?.OpenContainer(station.Hold), LcdHudTheme.Brass);
            openHold.style.minHeight = 28;
            p.Add(openHold);
            p.Add(T.Muted("The hold replaces this card; press E on the station to come back."));
            SteampunkTheme.Frame(p);
            return p;
        }

        // ── Switch ───────────────────────────────────────────────────────────
        public static VisualElement SwitchPanel(RailTrack track)
        {
            if (track == null) return T.MachinePanel();
            var p = T.MachinePanel();

            p.Add(Header("🔀", "Rail Switch",
                track.IsActiveJunction ? "JUNCTION" : "PLAIN LINE",
                track.IsActiveJunction ? T.AccentGreen : T.TextMuted));
            p.Add(SteampunkTheme.RivetedDivider());

            var links = track.Links;
            if (!track.IsActiveJunction)
            {
                p.Add(T.Muted("This switch has fewer than three connections, so it behaves as " +
                    "plain track. Lay a branch off it to make it a working junction."));
                return p;
            }

            p.Add(T.Subtitle("Route set to"));
            for (int i = 0; i < links.Count; i++)
            {
                int index = i;
                var link = links[i];
                if (link == null) continue;
                Vector3 dir = link.transform.position - track.transform.position;
                var b = SteampunkTheme.SelectorButton(
                    (index == track.SwitchSelection ? "◉  " : "○  ") + Describe(dir),
                    index == track.SwitchSelection,
                    () => { track.SetSwitchSelection(index); Refresh(); }, false);
                b.style.marginBottom = 4;
                b.style.minHeight = 26;
                p.Add(b);
            }

            p.Add(T.Spacer(4));
            p.Add(T.Muted("A train arriving from the selected leg takes the next available " +
                "route instead, so the points can never bounce a train straight back."));
            SteampunkTheme.Frame(p);
            return p;
        }

        private static string Describe(Vector3 direction)
        {
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
                return direction.x > 0f ? "EAST" : "WEST";
            return direction.z > 0f ? "NORTH" : "SOUTH";
        }

        // ── Footplate ────────────────────────────────────────────────────────
        public static VisualElement SteamPanel(GridSteamEngine engine, MachineUIs.SlotBuilder slot)
        {
            if (engine == null) return T.MachinePanel();
            var p = T.MachinePanel();

            p.Add(Header("🔥", "Steam Engine", engine.HasSteam
                    ? $"TURNING {engine.CurrentRPM:0} RPM"
                    : (engine.firing ? "RAISING STEAM" : "FIRE BANKED"),
                engine.HasSteam ? T.AccentGreen : T.AccentAmber));
            p.Add(SteampunkTheme.RivetedDivider());

            p.Add(SteampunkTheme.DialRow(
                SteampunkTheme.Dial("PRESSURE", engine.pressure, $"{engine.pressure * 100f:0}%",
                    112f, 0.8f),
                SteampunkTheme.Dial("WATER",
                    engine.waterCapacity > 0f ? engine.waterStored / engine.waterCapacity : 0f,
                    $"{engine.waterStored:0} L", 112f),
                SteampunkTheme.Dial("FLYWHEEL", engine.CurrentRPM / 220f,
                    $"{engine.CurrentRPM:0} RPM", 112f)
            ));
            p.Add(T.Muted("The engine's only product is rotation - the mechanical drive and " +
                "the brass screens take it from the flywheel."));
            p.Add(T.Spacer(4));

            p.Add(T.Subtitle("Firebox"));
            if (slot != null)
            {
                engine.EnsureFirebox();
                var grid = T.SlotGrid();
                grid.Add(slot(engine.firebox, 0, engine.firebox.GetSlot(0), false, true));
                p.Add(grid);
            }
            p.Add(T.Muted("Coal, or wood at half the patience. The firebox burns from this " +
                "slot first, then shovels out of any cargo container on the grid."));
            p.Add(T.Spacer(4));

            var fireRow = new VisualElement();
            fireRow.style.flexDirection = FlexDirection.Row;
            var fire = SteampunkTheme.SelectorButton(
                engine.firing ? "BANK THE FIRE" : "LIGHT THE FIRE", engine.firing,
                () => { engine.firing = !engine.firing; Refresh(); });
            fire.style.marginRight = 4;
            fireRow.Add(fire);
            var whistle = T.SmallButton("WHISTLE", () => engine.Whistle(), LcdHudTheme.Brass);
            whistle.style.minHeight = 24;
            whistle.style.minWidth = 90;
            fireRow.Add(whistle);
            p.Add(fireRow);

            p.Add(T.Spacer(4));
            p.Add(T.Muted("Tank wagons feed the boiler on the move; water towers fill it " +
                "berthed. A LOAD station with a coal filter coals the tender."));
            SteampunkTheme.Frame(p);
            return p;
        }

        // ── Schedule ─────────────────────────────────────────────────────────
        private static string _pickerStation = "";
        private static ScheduleWait _pickerWait = ScheduleWait.Seconds;
        private static float _pickerSeconds = 15f;

        public static VisualElement SchedulePanel(GridTrainScheduleBlock block)
        {
            if (block == null) return T.MachinePanel();
            var p = T.MachinePanel();

            p.Add(Header("📋", "Train Schedule", block.TrainName, T.AccentAmber));
            p.Add(SteampunkTheme.RivetedDivider());
            var status = new Label(block.StatusLabel);
            status.style.fontSize = 10;
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.color = new StyleColor(T.AccentAmber);
            status.style.marginBottom = 4;
            p.Add(status);

            p.Add(T.Subtitle("Stops"));
            var entries = block.schedule.entries;
            if (entries.Count == 0)
                p.Add(T.Muted("No stops yet. Add one below - a train with no schedule is a " +
                    "train somebody drives by hand."));

            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                var entry = entries[i];

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 4;

                var label = new Label($"{i + 1}.  {ScheduleConditions.Describe(entry)}");
                label.style.flexGrow = 1;
                label.style.fontSize = 11;
                label.style.color = new StyleColor(
                    i == block.CurrentIndex ? T.AccentCyan : T.TextSecondary);
                row.Add(label);

                var up = T.SmallButton("UP", () => Move(block, index, -1), T.BgSlot);
                up.style.minWidth = 40;
                up.style.marginLeft = 3;
                row.Add(up);
                var dn = T.SmallButton("DN", () => Move(block, index, +1), T.BgSlot);
                dn.style.minWidth = 40;
                dn.style.marginLeft = 3;
                row.Add(dn);
                var del = T.SmallButton("DEL", () => { entries.RemoveAt(index); Refresh(); }, T.BgSlot);
                del.style.minWidth = 40;
                del.style.marginLeft = 3;
                row.Add(del);
                p.Add(row);
            }

            p.Add(T.Spacer(4));
            p.Add(T.Subtitle("Add a stop"));

            if (string.IsNullOrEmpty(_pickerStation))
            {
                p.Add(T.Muted("Pick the station this stop serves:"));
                var stations = RailStation.All;
                int shown = 0;
                for (int i = 0; i < stations.Count && shown < 10; i++)
                {
                    var st = stations[i];
                    if (st == null) continue;
                    shown++;
                    string name = st.StationName;
                    var b = SteampunkTheme.SelectorButton(name, false,
                        () => { _pickerStation = name; Refresh(); }, false);
                    b.style.marginBottom = 4;
                    b.style.minHeight = 24;
                    p.Add(b);
                }
                if (shown == 0)
                    p.Add(T.Muted("No stations exist yet. Build a Rail Station first."));
            }
            else
            {
                var dest = new Label("Stop at: " + _pickerStation);
                dest.style.fontSize = 11;
                dest.style.unityFontStyleAndWeight = FontStyle.Bold;
                dest.style.color = new StyleColor(T.TextPrimary);
                dest.style.marginBottom = 4;
                p.Add(dest);

                var waitRow = new VisualElement();
                waitRow.style.flexDirection = FlexDirection.Row;
                waitRow.style.flexWrap = Wrap.Wrap;
                waitRow.style.marginBottom = 4;
                foreach (ScheduleWait wait in System.Enum.GetValues(typeof(ScheduleWait)))
                {
                    var w = wait;
                    var b = SteampunkTheme.SelectorButton(w switch
                    {
                        ScheduleWait.HoldFull => "HOLD FULL",
                        ScheduleWait.HoldEmpty => "HOLD EMPTY",
                        ScheduleWait.HoldHasSpace => "HOLD HAS SPACE",
                        ScheduleWait.TrainEmpty => "TRAIN EMPTY",
                        ScheduleWait.TrainFull => "TRAIN FULL",
                        _ => "DWELL",
                    }, _pickerWait == w, () => { _pickerWait = w; Refresh(); }, false);
                    b.style.marginRight = 4;
                    b.style.marginBottom = 4;
                    waitRow.Add(b);
                }
                p.Add(waitRow);

                if (_pickerWait == ScheduleWait.Seconds)
                {
                    var field = new TextField { value = _pickerSeconds.ToString("0") };
                    field.style.minHeight = 26;
                    field.style.marginBottom = 4;
                    SteampunkTheme.GuardTextField(field);
                    field.RegisterValueChangedCallback(evt =>
                        float.TryParse(evt.newValue, out _pickerSeconds));
                    p.Add(field);
                }

                var addRow = new VisualElement();
                addRow.style.flexDirection = FlexDirection.Row;
                var add = SteampunkTheme.SelectorButton("ADD STOP", true, () =>
                {
                    block.schedule.entries.Add(new ScheduleEntry(_pickerStation, _pickerWait, _pickerSeconds));
                    _pickerStation = "";
                    Refresh();
                });
                add.style.marginRight = 4;
                add.style.minHeight = 28;
                addRow.Add(add);
                var back = SteampunkTheme.SelectorButton("CHANGE STATION", false,
                    () => { _pickerStation = ""; Refresh(); }, false);
                back.style.minHeight = 28;
                addRow.Add(back);
                p.Add(addRow);
            }
            SteampunkTheme.Frame(p);
            return p;
        }

        private static void Move(GridTrainScheduleBlock block, int index, int delta)
        {
            var entries = block.schedule.entries;
            int target = index + delta;
            if (target < 0 || target >= entries.Count) return;
            (entries[index], entries[target]) = (entries[target], entries[index]);
            Refresh();
        }

        // ── Display ──────────────────────────────────────────────────────────
        public static VisualElement DisplayPanel(RailDisplayScreen screen)
        {
            if (screen == null) return T.MachinePanel();
            var p = T.MachinePanel();

            bool mains = screen.GetComponent<VoxelEngine.Power.PowerConsumer>() != null;
            p.Add(Header("🖥", "Display Console", screen.IsPowered
                    ? (mains ? "ENGINE RUNNING" : "SHAFT TURNING")
                    : (mains ? "NO POWER" : "SHAFT STOPPED"),
                screen.IsPowered ? T.AccentGreen : T.AccentAmber));
            p.Add(SteampunkTheme.RivetedDivider());

            p.Add(T.Muted(mains
                ? "Mains-fed: a small electric engine inside turns the drums."
                : "Shaft-fed: this screen lives while anything on the grid turns."));
            p.Add(T.Spacer(4));

            p.Add(T.Subtitle("Display kind"));
            var kindRow = new VisualElement();
            kindRow.style.flexDirection = FlexDirection.Row;
            kindRow.style.marginBottom = 4;
            foreach (ScreenKind k in System.Enum.GetValues(typeof(ScreenKind)))
            {
                var kind = k;
                var b = SteampunkTheme.SelectorButton(kind switch
                {
                    ScreenKind.Nixie => "NIXIE",
                    ScreenKind.Analog => "ANALOG DIAL",
                    _ => "SPLIT-FLAP",
                }, screen.Kind == kind,
                    () => { screen.Configure(kind, screen.Source, screen.customText); Refresh(); });
                b.style.marginRight = 4;
                kindRow.Add(b);
            }
            p.Add(kindRow);

            p.Add(T.Subtitle("Data source"));
            foreach (ScreenSource src in System.Enum.GetValues(typeof(ScreenSource)))
            {
                var source = src;
                var b = SteampunkTheme.SelectorButton(source switch
                {
                    ScreenSource.TrainSpeed => "TRAIN SPEED",
                    ScreenSource.ConsistLoad => "CONSIST LOAD",
                    ScreenSource.Departures => "DEPARTURES",
                    ScreenSource.StationStatus => "STATION STATUS",
                    _ => "CUSTOM TEXT",
                }, screen.Source == source,
                    () => { screen.Configure(screen.Kind, source, screen.customText); Refresh(); }, false);
                b.style.marginBottom = 4;
                b.style.minHeight = 24;
                p.Add(b);
            }

            if (screen.Source == ScreenSource.CustomText)
            {
                p.Add(T.Subtitle("Text"));
                var field = new TextField { value = screen.customText, multiline = true };
                field.style.marginBottom = 4;
                SteampunkTheme.GuardTextField(field);
                field.RegisterValueChangedCallback(evt =>
                {
                    screen.customText = evt.newValue ?? "";
                    screen.Configure(screen.Kind, screen.Source, screen.customText);
                });
                p.Add(field);
            }
            SteampunkTheme.Frame(p);
            return p;
        }
    }
}
