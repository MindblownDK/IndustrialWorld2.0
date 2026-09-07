// Assets/Scripts/VoxelEngine/UI/VitalsHud.cs
//
// Compact fitted life-support monitor. Uses the shared LCD instrument language
// instead of generic rounded colour pills: practical labels, phosphor segments,
// and a quiet physical chassis in the bottom-right corner.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Player;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class VitalsHud
    {
        private const int SegmentCount = 8;

        private static VisualElement _root, _container;
        private static VisualElement[] _hpSegments, _h2Segments, _hungerSegments, _oxySegments, _pwrSegments;
        private static Label _hpVal, _h2Val, _hungerVal, _oxyVal, _pwrVal;
        private static Label _oxyCode;
        private static VisualElement[] _tankSegments;
        private static Label _tankVal;
        private static VisualElement _tankRow;
        private static VisualElement _heatRow;
        private static Label _heatLabel;
        private static float _prevTank;
        private static VisualElement _pwrRow;
        private static VisualElement _roomRow;
        private static Label _roomLabel;

        private static float _prevHp, _prevH2, _prevHunger, _prevOxy, _prevPwr;

        // Used by the held paint monitor so it clears this larger instrument chassis.
        // Compacted in 7.13.3: tighter rows, same information, less screen space.
        public const float TOTAL_HEIGHT = 161f;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            _container = new VisualElement { name = "VitalsHud" };
            _container.style.position = Position.Absolute;
            _container.style.bottom = 16;
            _container.style.right = 18;
            _container.style.width = 224;
            _container.style.paddingLeft = 8;
            _container.style.paddingRight = 8;
            _container.style.paddingTop = 7;
            _container.style.paddingBottom = 8;
            _container.style.flexDirection = FlexDirection.Column;
            _container.style.alignItems = Align.Stretch;
            _container.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyChassis(_container, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.94f), 3f);
            uiRoot.Add(_container);
            LcdHudTheme.AnimateScreenBoot(_container);
            // Yield to machine/chest panels so the right-side cluster never overlaps.
            LcdHudTheme.YieldWhileBlocking(_container);

            BuildHeader();
            var hpRow = AddVitalRow("HP", T.AccentRed);
            _hpSegments = hpRow.segments;
            _hpVal = hpRow.value;
            AddGap(2);
            var h2Row = AddVitalRow("H₂", new Color(0.42f, 0.78f, 0.72f));
            _h2Segments = h2Row.segments;
            _h2Val = h2Row.value;
            AddGap(2);
            var hungerRow = AddVitalRow("HNG", T.AccentAmber);
            _hungerSegments = hungerRow.segments;
            _hungerVal = hungerRow.value;
            AddGap(2);
            var oxygenRow = AddVitalRow("O₂", LcdHudTheme.Phosphor);
            _oxySegments = oxygenRow.segments;
            _oxyVal = oxygenRow.value;
            _oxyCode = oxygenRow.code;
            AddGap(2);
            // Suit reserve: only shown while an oxygen tank is actually equipped.
            var tankRow = AddVitalRow("TNK", new Color(0.45f, 0.80f, 0.95f), out _tankRow);
            _tankSegments = tankRow.segments;
            _tankVal = tankRow.value;
            _tankRow.style.display = DisplayStyle.None;
            AddGap(2);
            var powerRow = AddVitalRow("PWR", new Color(0.64f, 0.86f, 0.44f), out _pwrRow);
            _pwrSegments = powerRow.segments;
            _pwrVal = powerRow.value;
            _pwrRow.style.display = DisplayStyle.None;
            BuildRoomRow();
            BuildHeatRow();

            _prevHp = _prevH2 = _prevHunger = _prevOxy = _prevPwr = _prevTank = -1f;
        }

        /// <summary>Sealed-room pressure strip. Hidden entirely when the player is not
        /// standing in a detected compartment, so the HUD stays quiet outdoors.</summary>
        private static void BuildRoomRow()
        {
            _roomRow = new VisualElement { name = "VitalLcd_ROOM" };
            _roomRow.style.height = 16;
            _roomRow.style.marginTop = 3;
            _roomRow.style.paddingLeft = 5;
            _roomRow.style.paddingRight = 5;
            _roomRow.style.flexDirection = FlexDirection.Row;
            _roomRow.style.alignItems = Align.Center;
            _roomRow.style.justifyContent = Justify.SpaceBetween;
            _roomRow.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(_roomRow, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.85f), 1f);

            var code = new Label("ROOM");
            code.style.fontSize = 8;
            code.style.letterSpacing = 1f;
            code.style.unityFontStyleAndWeight = FontStyle.Bold;
            code.style.color = new StyleColor(T.TextMuted);
            code.pickingMode = PickingMode.Ignore;
            _roomRow.Add(code);

            _roomLabel = new Label("—");
            _roomLabel.style.fontSize = 8;
            _roomLabel.style.letterSpacing = 0.6f;
            _roomLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _roomLabel.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _roomLabel.pickingMode = PickingMode.Ignore;
            _roomRow.Add(_roomLabel);

            _roomRow.style.display = DisplayStyle.None;
            _container.Add(_roomRow);
        }

        /// <summary>Hull thermal strip. Appears only when the ship the player is riding
        /// actually starts heating, so it reads as an alarm rather than clutter.</summary>
        private static void BuildHeatRow()
        {
            _heatRow = new VisualElement { name = "VitalLcd_HEAT" };
            _heatRow.style.height = 16;
            _heatRow.style.marginTop = 3;
            _heatRow.style.paddingLeft = 5;
            _heatRow.style.paddingRight = 5;
            _heatRow.style.flexDirection = FlexDirection.Row;
            _heatRow.style.alignItems = Align.Center;
            _heatRow.style.justifyContent = Justify.SpaceBetween;
            _heatRow.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(_heatRow, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.85f), 1f);

            var code = new Label("HULL");
            code.style.fontSize = 8;
            code.style.letterSpacing = 1f;
            code.style.unityFontStyleAndWeight = FontStyle.Bold;
            code.style.color = new StyleColor(T.TextMuted);
            code.pickingMode = PickingMode.Ignore;
            _heatRow.Add(code);

            _heatLabel = new Label("—");
            _heatLabel.style.fontSize = 8;
            _heatLabel.style.letterSpacing = 0.6f;
            _heatLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _heatLabel.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _heatLabel.pickingMode = PickingMode.Ignore;
            _heatRow.Add(_heatLabel);

            _heatRow.style.display = DisplayStyle.None;
            _container.Add(_heatRow);
        }

        private static void TickHeatRow()
        {
            if (_heatRow == null || _heatLabel == null) return;

            var player = PlayerStats.Instance;
            var thermal = player != null
                ? VoxelEngine.Thermal.ThermalService.NearestTo(player.transform.position, 40f)
                : null;

            // Stay hidden until the hull is at least warm — no strip during normal flight.
            var band = thermal != null
                ? VoxelEngine.Thermal.ThermalRules.Band(thermal.PeakTemperatureC)
                : VoxelEngine.Thermal.ThermalBand.Nominal;

            if (thermal == null || band == VoxelEngine.Thermal.ThermalBand.Nominal)
            {
                if (_heatRow.style.display != DisplayStyle.None)
                    _heatRow.style.display = DisplayStyle.None;
                return;
            }

            if (_heatRow.style.display != DisplayStyle.Flex)
                _heatRow.style.display = DisplayStyle.Flex;

            string label = VoxelEngine.Thermal.ThermalRules.BandLabel(band);
            string entry = thermal.EntryHeatingC > 1f ? "  RE-ENTRY" : string.Empty;
            _heatLabel.text = $"{label}  {thermal.PeakTemperatureC:0}\u00B0C{entry}";
            _heatLabel.style.color = new StyleColor(VoxelEngine.Thermal.ThermalRules.BandColor(band));
        }

        private static void TickRoomRow()
        {
            if (_roomRow == null || _roomLabel == null) return;

            var player = PlayerStats.Instance;
            var room = player != null
                ? VoxelEngine.Pressure.RoomAtmosphereService.RoomAt(player.transform.position)
                : null;

            if (room == null)
            {
                if (_roomRow.style.display != DisplayStyle.None)
                    _roomRow.style.display = DisplayStyle.None;
                return;
            }

            if (_roomRow.style.display != DisplayStyle.Flex)
                _roomRow.style.display = DisplayStyle.Flex;

            Color tone = room.IsBreathable ? LcdHudTheme.Phosphor
                : room.PressureAtm > 0.02f ? T.AccentAmber : T.AccentRed;
            // Crew count is only worth the pixels when someone else is sharing the air.
            string crew = room.Occupants > 1 ? $"  ×{room.Occupants}" : string.Empty;
            _roomLabel.text = $"{room.StatusLabel}  {room.PressureAtm * 100f:0}%{crew}";
            _roomLabel.style.color = new StyleColor(tone);
        }

        private static void BuildHeader()
        {
            var row = new VisualElement { name = "VitalsHeader" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;
            row.pickingMode = PickingMode.Ignore;
            _container.Add(row);

            var title = new Label("SUIT STATUS");
            title.style.flexGrow = 1;
            title.style.fontSize = 8;
            title.style.letterSpacing = 1.25f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextSecondary);
            title.pickingMode = PickingMode.Ignore;
            row.Add(title);

            var id = new Label("LSS-01");
            id.style.fontSize = 7;
            id.style.letterSpacing = 0.7f;
            id.style.unityFontStyleAndWeight = FontStyle.Bold;
            id.style.color = new StyleColor(T.TextMuted);
            id.pickingMode = PickingMode.Ignore;
            row.Add(id);
        }

        public static void Tick()
        {
            var stats = PlayerStats.Instance;
            if (stats == null || _hpSegments == null) return;

            UpdateValue(_hpSegments, _hpVal, stats.Health, stats.MaxHealth, ref _prevHp, T.AccentRed, false);

            GetPlayerHydrogenMl(out float h2Current, out float h2Capacity);
            UpdateValue(_h2Segments, _h2Val, h2Current, h2Capacity, ref _prevH2,
                new Color(0.42f, 0.78f, 0.72f), true);

            UpdateValue(_hungerSegments, _hungerVal, stats.Hunger, stats.MaxHunger, ref _prevHunger, T.AccentAmber, false);

            Color oxygenColor = stats.CurrentOxygenEnvironment switch
            {
                OxygenEnvironment.Vacuum => T.AccentRed,
                OxygenEnvironment.Underwater => T.AccentAmber,
                _ => LcdHudTheme.Phosphor,
            };
            UpdateValue(_oxySegments, _oxyVal, stats.Oxygen, stats.MaxOxygen, ref _prevOxy, oxygenColor, false);
            if (_oxyCode != null) _oxyCode.style.color = new StyleColor(oxygenColor);

            TickSuitTankRow();
            TickPowerRow();
            TickRoomRow();
            TickHeatRow();
        }

        private static (VisualElement[] segments, Label value, Label code) AddVitalRow(string code, Color signalColor)
            => AddVitalRow(code, signalColor, out _);

        private static (VisualElement[] segments, Label value, Label code) AddVitalRow(string code, Color signalColor,
            out VisualElement row)
        {
            row = new VisualElement { name = "VitalLcd_" + code };
            row.style.height = 20;
            row.style.paddingLeft = 5;
            row.style.paddingRight = 5;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(row, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.85f), 1f);
            _container.Add(row);
            LcdHudTheme.AddAnimatedScanlines(row, 2, 4f, 10f);

            var codeLabel = new Label(code);
            codeLabel.style.width = 27;
            codeLabel.style.fontSize = 8;
            codeLabel.style.letterSpacing = 0.85f;
            codeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            codeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            codeLabel.style.alignSelf = Align.Center;
            codeLabel.style.color = new StyleColor(signalColor);
            codeLabel.pickingMode = PickingMode.Ignore;
            row.Add(codeLabel);

            var track = LcdHudTheme.CreateSegmentTrack(SegmentCount, out var segments, 11f);
            track.style.flexGrow = 1;
            track.style.marginRight = 6;
            track.style.alignSelf = Align.Center;
            row.Add(track);

            var value = new Label("—");
            value.style.width = 48;
            value.style.fontSize = 9;
            value.style.letterSpacing = 0.35f;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.alignSelf = Align.Center;
            value.style.color = new StyleColor(signalColor);
            value.pickingMode = PickingMode.Ignore;
            row.Add(value);

            return (segments, value, codeLabel);
        }

        private static void UpdateValue(VisualElement[] segments, Label value, float current, float maximum,
            ref float previous, Color nominalColor, bool millilitres)
        {
            float fill = maximum > 0.0001f ? Mathf.Clamp01(current / maximum) : 0f;
            Color signal = ResolveSignal(fill, nominalColor);
            LcdHudTheme.SetSegments(segments, fill, signal);
            if (value != null) value.style.color = new StyleColor(signal);

            if (!Mathf.Approximately(current, previous))
            {
                if (value != null)
                    value.text = millilitres ? FormatMl(current) : Mathf.RoundToInt(current).ToString();
                previous = current;
            }
        }

        private static void TickPowerRow()
        {
            bool hasPower = GetPlayerPowerMl(out int currentWh, out int capacityWh);
            if (_pwrRow != null)
                _pwrRow.style.display = hasPower ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasPower) { _prevPwr = -1f; return; }

            float fill = capacityWh > 0 ? Mathf.Clamp01(currentWh / (float)capacityWh) : 0f;
            Color signal = ResolveSignal(fill, new Color(0.64f, 0.86f, 0.44f));
            LcdHudTheme.SetSegments(_pwrSegments, fill, signal);
            if (_pwrVal != null)
            {
                _pwrVal.style.color = new StyleColor(signal);
                if (_prevPwr < 0f || !Mathf.Approximately(_prevPwr, currentWh))
                {
                    _pwrVal.text = $"{fill * 100f:0}%";
                    _prevPwr = currentWh;
                }
            }
        }

        private static Color ResolveSignal(float fill, Color nominal)
        {
            if (fill > 0.50f) return nominal;
            if (fill > 0.25f) return Color.Lerp(T.AccentAmber, nominal, (fill - 0.25f) / 0.25f);
            return Color.Lerp(T.AccentRed, T.AccentAmber, fill / 0.25f);
        }

        /// <summary>
        /// Suit oxygen reserve strip. Hidden unless a tank is equipped, and it turns
        /// amber then red as the reserve runs down so the player gets a warning while
        /// there is still time to get back inside.
        /// </summary>
        private static void TickSuitTankRow()
        {
            if (_tankRow == null) return;

            var equipment = FindPlayerEquipment();
            float cap = equipment != null ? equipment.SuitTankCapacityLitres : 0f;
            if (equipment == null || cap <= 0f)
            {
                if (_tankRow.style.display != DisplayStyle.None)
                    _tankRow.style.display = DisplayStyle.None;
                _prevTank = -1f;
                return;
            }

            if (_tankRow.style.display != DisplayStyle.Flex)
                _tankRow.style.display = DisplayStyle.Flex;

            float litres = equipment.SuitTankLitres;
            UpdateValue(_tankSegments, _tankVal, litres, cap, ref _prevTank,
                ResolveSignal(cap > 0f ? litres / cap : 0f, new Color(0.45f, 0.80f, 0.95f)), false);
        }

        /// <summary>All hydrogen the player can currently burn: portable tanks plus equipped pack fuel.</summary>
        public static void GetPlayerHydrogenMl(out float currentMl, out float capacityMl)
        {
            GetInventoryHydrogenMl(out currentMl, out capacityMl);
            var equipment = FindPlayerEquipment();
            if (equipment == null) return;
            var summary = equipment.GetJetpackSummary();
            currentMl += summary.h2;
            capacityMl += summary.h2Cap;
        }

        public static void GetInventoryHydrogenMl(out float currentMl, out float capacityMl)
        {
            currentMl = 0f;
            capacityMl = 0f;
            var inventory = FindInventory();
            if (inventory == null || inventory.container == null) return;
            inventory.container.EnsureValid();
            for (int i = 0; i < inventory.container.Size; i++)
            {
                var stack = inventory.container.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (!HydrogenCanisterItem.IsPortableHydrogenTank(stack.item)) continue;
                currentMl += HydrogenCanisterItem.GetStoredMl(stack);
                capacityMl += HydrogenCanisterItem.GetCapacityMl(stack);
            }
        }

        private static Inventory _cachedInventory;

        private static Inventory FindInventory()
        {
            if (_cachedInventory == null) _cachedInventory = Object.FindAnyObjectByType<Inventory>();
            return _cachedInventory;
        }

        private static PlayerEquipment FindPlayerEquipment()
        {
            var inventory = FindInventory();
            return inventory != null ? inventory.GetComponent<PlayerEquipment>() : null;
        }

        private static bool GetPlayerPowerMl(out int currentWh, out int capacityWh)
        {
            currentWh = 0;
            capacityWh = 0;
            var equipment = FindPlayerEquipment();
            if (equipment != null)
            {
                var summary = equipment.GetJetpackSummary();
                if (summary.anyPack && summary.powerCap > 0)
                {
                    currentWh += summary.power;
                    capacityWh += summary.powerCap;
                }
            }

            var inventory = FindInventory();
            if (inventory != null && inventory.container != null)
            {
                inventory.container.EnsureValid();
                for (int i = 0; i < inventory.container.Size; i++)
                {
                    var stack = inventory.container.GetSlot(i);
                    if (stack == null || stack.IsEmpty || stack.item == null) continue;
                    if (!PortableBatteryItem.IsPortableBattery(stack.item)) continue;
                    currentWh += PortableBatteryItem.GetStoredMl(stack);
                    capacityWh += PortableBatteryItem.GetCapacityMl(stack);
                }
            }
            return capacityWh > 0;
        }

        private static string FormatMl(float millilitres)
        {
            if (millilitres >= 1000f) return $"{millilitres / 1000f:0.0}L";
            return $"{Mathf.RoundToInt(millilitres)}ml";
        }

        private static void AddGap(float height)
        {
            var gap = new VisualElement();
            gap.style.height = height;
            gap.pickingMode = PickingMode.Ignore;
            _container.Add(gap);
        }
    }
}
