// Assets/Scripts/VoxelEngine/UI/BombHud.cs
//
// Bomb timer UI (17.45.0-dev):
//   • A FUSE WHEEL on the shared build key while an explosive is in hand. Hold, flick,
//     release - the same gesture as every other dial in the game. It writes
//     ExplosiveBlock.NextFuse, so the next charge placed carries that fuse. The list runs
//     from a one-second contact charge to a thirty-second demolition timer, plus MANUAL:
//     armed, live, and with no countdown at all until it takes a hit.
//   • A quiet READOUT pill while a bomb is held, so the chosen fuse is visible without
//     opening anything.
//   • A floating COUNTDOWN NUMBER over each placed bomb in view (green→red), which reads
//     MAN over a charge that is armed by hand.
//
// The old version put a slider on screen permanently. A slider is a thing you have to
// read, aim at and drag while holding something that explodes; a wheel is one flick.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Combat;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class BombHud
    {
        /// <summary>Every fuse the wheel offers, in seconds. The last one is MANUAL.</summary>
        private static readonly float[] FuseChoices =
        {
            1f, 2f, 3f, 5f, 10f, 20f, 30f, ExplosiveBlock.ManualFuse,
        };

        /// <summary>What an unset NextFuse means when the held prefab cannot be read.</summary>
        private const float FallbackFuse = 5f;

        private static VisualElement _root;
        private static VisualElement _readout;
        private static Label _readoutLabel;
        private static VisualElement _timerLayer;
        private static readonly List<Label> _labels = new();
        private const int MAX_LABELS = 8;

        private static readonly RadialWheelController _fuseWheel = new();
        private static bool _wheelWired;

        private static float _scanAcc;
        private static readonly List<MonoBehaviour> _bombs = new();

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;

            // Re-add if the root was cleared underneath us.
            if (_readoutRow != null && _readoutRow.parent == null && _root == uiRoot) uiRoot.Add(_readoutRow);
            if (_timerLayer != null && _timerLayer.parent == null && _root == uiRoot) uiRoot.Add(_timerLayer);
            if (_root == uiRoot && _readout != null && _readout.parent == uiRoot) return;

            _root = uiRoot;
            if (_readoutRow != null) _readoutRow.RemoveFromHierarchy();
            if (_timerLayer != null) _timerLayer.RemoveFromHierarchy();

            WireWheel();

            // ── Fuse readout (bottom-centre, only while holding a bomb). ──
            // The row is what centres the pill: an absolutely positioned element is placed
            // by its edges, so alignSelf has nothing to work with and the pill would sit in
            // the left corner on a wide screen.
            _readoutRow = new VisualElement { name = "BombFuseRow" };
            _readoutRow.style.position = Position.Absolute;
            _readoutRow.style.left = 0; _readoutRow.style.right = 0;
            _readoutRow.style.bottom = 84;
            _readoutRow.style.flexDirection = FlexDirection.Row;
            _readoutRow.style.justifyContent = Justify.Center;
            _readoutRow.pickingMode = PickingMode.Ignore;

            _readout = new VisualElement { name = "BombFuseReadout" };
            _readout.style.flexDirection = FlexDirection.Row;
            _readout.style.alignItems = Align.Center;
            _readout.style.backgroundColor = new StyleColor(new Color(0.05f, 0.06f, 0.09f, 0.82f));
            _readout.style.paddingTop = 5; _readout.style.paddingBottom = 5;
            _readout.style.paddingLeft = 12; _readout.style.paddingRight = 12;
            T.Radius(_readout, 6); T.Border(_readout, 1, new Color(1f, 0.55f, 0.15f, 0.45f));
            _readoutRow.style.display = DisplayStyle.None;

            var title = new Label("FUSE");
            title.style.color = new Color(1f, 0.62f, 0.22f);
            title.style.fontSize = 10; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginRight = 8;
            _readout.Add(title);

            _readoutLabel = new Label(FormatFuse(FallbackFuse));
            _readoutLabel.style.color = Color.white;
            _readoutLabel.style.fontSize = 13; _readoutLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _readoutLabel.style.marginRight = 10;
            _readout.Add(_readoutLabel);

            _readoutHint = new Label("hold " + VoxelEngine.Settings.GameSettings.GetKey(InputAction.BuildWheel));
            _readoutHint.style.color = new Color(0.62f, 0.68f, 0.74f);
            _readoutHint.style.fontSize = 10;
            _readout.Add(_readoutHint);
            _readoutRow.Add(_readout);
            uiRoot.Add(_readoutRow);

            // ── Countdown-number layer (full-screen overlay, non-interactive). ──
            _timerLayer = new VisualElement { name = "BombTimerLayer" };
            _timerLayer.style.position = Position.Absolute;
            _timerLayer.style.left = 0; _timerLayer.style.top = 0; _timerLayer.style.right = 0; _timerLayer.style.bottom = 0;
            _timerLayer.pickingMode = PickingMode.Ignore;
            _labels.Clear();
            for (int i = 0; i < MAX_LABELS; i++)
            {
                var l = new Label("");
                l.style.position = Position.Absolute;
                l.style.fontSize = 16; l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.color = Color.white;
                l.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.55f));
                l.style.paddingLeft = 5; l.style.paddingRight = 5; l.style.paddingTop = 1; l.style.paddingBottom = 1;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                T.Radius(l, 4);
                l.style.display = DisplayStyle.None;
                _timerLayer.Add(l);
                _labels.Add(l);
            }
            uiRoot.Add(_timerLayer);
        }

        /// <summary>Wires the fuse wheel once. It reads the live NextFuse, so nothing here
        /// has to be pushed at it when a bomb is placed.</summary>
        private static void WireWheel()
        {
            if (_wheelWired) return;
            _wheelWired = true;

            _fuseWheel.ResolveHost = () => _root;
            _fuseWheel.BuildOptions = FillFuseOptions;
            _fuseWheel.ConfirmOption = ConfirmFuse;
            _fuseWheel.GroupLabel = "CHARGE FUSE";
            _fuseWheel.IdleTitle = "KEEP FUSE";
            _fuseWheel.IdleDescription = "Release in the centre to leave the fuse as it is";
            _fuseWheel.IdleTitleColor = new Color(1f, 0.62f, 0.22f);
            // A tap means "never mind". A pinned dial would leave the world unclickable
            // while the player is holding something that explodes.
            _fuseWheel.AllowTapPin = false;
        }

        private static void FillFuseOptions(List<RadialOption> options)
        {
            if (options == null) return;

            // The first wedge is the prefab's OWN fuse, so a player who has never chosen
            // anything is shown that they are on the default and can come back to it after
            // trying a timer. Nothing is written until a wedge is picked: baking a value in
            // here would quietly turn the antimatter charge's eight-second fuse into five.
            options.Add(new RadialOption
            {
                Title = "DEFAULT",
                Description = "Whatever this charge is authored with",
                Icon = null,
                Available = true,
                Affordable = true,
                Selected = ExplosiveBlock.NextFuse < 0f,
            });

            float current = ExplosiveBlock.NextFuse;
            for (int i = 0; i < FuseChoices.Length; i++)
            {
                float choice = FuseChoices[i];
                bool manual = choice == ExplosiveBlock.ManualFuse;
                options.Add(new RadialOption
                {
                    Title = manual ? "MANUAL" : FormatFuse(choice),
                    Description = manual
                        ? "Arms the charge with no countdown - it waits for a hit"
                        : choice <= 2f ? "Contact charge: plant it and run"
                        : choice <= 5f ? "The demolition standard"
                        : "Enough time to reach the next room",
                    Icon = null,
                    Available = true,
                    Affordable = true,
                    Selected = ExplosiveBlock.NextFuse >= 0f && Mathf.Abs(current - choice) < 0.01f,
                });
            }
        }

        private static bool ConfirmFuse(int index)
        {
            if (index == 0)
            {
                ExplosiveBlock.NextFuse = -1f;
                VoxelEngine.FX.AudioManager.PlayUI(
                    VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.UiClick), 0.6f, 0.94f);
                BuildFeedbackHud.Show("Charge fuse", "Back to the charge's own fuse",
                    null, new Color(1f, 0.62f, 0.22f));
                return true;
            }

            int choice = index - 1;
            if (choice < 0 || choice >= FuseChoices.Length) return false;
            ExplosiveBlock.NextFuse = FuseChoices[choice];
            VoxelEngine.FX.AudioManager.PlayUI(
                VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.UiClick), 0.6f, 1.04f);
            BuildFeedbackHud.Show("Charge fuse",
                FuseChoices[choice] == ExplosiveBlock.ManualFuse
                    ? "MANUAL - the charge waits until it takes a hit"
                    : FormatFuse(FuseChoices[choice]) + " after placement",
                null, new Color(1f, 0.62f, 0.22f));
            return true;
        }

        /// <summary>The fuse the next charge will actually use.</summary>
        private static float EffectiveFuse(float heldDefault)
        {
            if (ExplosiveBlock.NextFuse >= 0f) return ExplosiveBlock.NextFuse;
            return heldDefault > 0f ? heldDefault : FallbackFuse;
        }

        private static string FormatFuse(float fuse)
            => fuse == ExplosiveBlock.ManualFuse ? "MANUAL" : Mathf.RoundToInt(fuse) + " s";

        /// <summary>The readout's text, naming the default when nothing explicit is set.</summary>
        private static string DescribeEffective(float heldDefault)
            => ExplosiveBlock.NextFuse < 0f
                ? "DEFAULT · " + FormatFuse(EffectiveFuse(heldDefault))
                : FormatFuse(ExplosiveBlock.NextFuse);

        public static void Tick(VoxelEngine.Items.Inventory inventory)
        {
            if (_readout == null) return;

            // ── Is an explosive in hand, and what is its own fuse? ──
            bool holdingBomb = false;
            float heldDefault = -1f;
            if (inventory != null && inventory.container != null)
            {
                var st = inventory.ActiveStack;
                if (st != null && !st.IsEmpty && st.item is VoxelEngine.Items.BlockItem bi && bi.placedPrefab != null)
                {
                    var keg = bi.placedPrefab.GetComponent<ExplosiveBlock>();
                    var antimatter = bi.placedPrefab.GetComponent<AntimatterBomb>();
                    if (keg != null) { holdingBomb = true; heldDefault = keg.fuse; }
                    else if (antimatter != null) { holdingBomb = true; heldDefault = antimatter.fuse; }
                }
            }

            // ── The wheel owns the build key while a bomb is in hand. ──
            WireWheel();
            _fuseWheel.Tick(InputAction.BuildWheel, holdingBomb);

            if (holdingBomb && !_fuseWheel.IsOpen && !UIState.IsBlocking)
                _fuseWheel.ShowPrompt("[" + VoxelEngine.Settings.GameSettings.GetKey(InputAction.BuildWheel) +
                    "]  CHARGE FUSE  ·  " + DescribeEffective(heldDefault));
            else
                _fuseWheel.HidePrompt();

            // ── Readout pill. ──
            if (holdingBomb && !_fuseWheel.IsOpen)
            {
                if (_readoutRow.style.display == DisplayStyle.None) _readoutRow.style.display = DisplayStyle.Flex;
                string text = DescribeEffective(heldDefault);
                if (_readoutLabel.text != text) _readoutLabel.text = text;
                bool manual = EffectiveFuse(heldDefault) == ExplosiveBlock.ManualFuse;
                _readoutLabel.style.color = manual ? new Color(1f, 0.42f, 0.25f) : Color.white;
                // Follows a rebind without needing the HUD mounted again.
                string hint = "hold " + VoxelEngine.Settings.GameSettings.GetKey(InputAction.BuildWheel);
                if (_readoutHint.text != hint) _readoutHint.text = hint;
            }
            else if (_readoutRow.style.display != DisplayStyle.None)
            {
                _readoutRow.style.display = DisplayStyle.None;
            }

            // ── Floating countdown numbers over placed bombs in view (throttled scan). ──
            _scanAcc += Time.unscaledDeltaTime;
            bool scan = _scanAcc >= 0.1f;
            if (scan) _scanAcc = 0f;

            Camera cam = Camera.main;
            int li = 0;
            if (cam != null)
            {
                if (scan)
                {
                    _bombs.Clear();
                    foreach (var k in Object.FindObjectsByType<ExplosiveBlock>())
                        if (k != null && k.isActiveAndEnabled) _bombs.Add(k);
                    foreach (var a in Object.FindObjectsByType<AntimatterBomb>())
                        if (a != null && a.isActiveAndEnabled) _bombs.Add(a);
                }

                Vector3 camPos = cam.transform.position;
                foreach (var b in _bombs)
                {
                    if (b == null) continue;
                    Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(b.transform.position);
                    Vector3 bp = b.transform.position + up * 2.2f;
                    if (Vector3.Distance(camPos, bp) > 60f) continue;
                    Vector3 sp = cam.WorldToScreenPoint(bp);
                    if (sp.z <= 0f || sp.x < 0 || sp.x > Screen.width || sp.y < 0 || sp.y > Screen.height) continue;
                    if (li >= MAX_LABELS) break;

                    bool manual = b is AntimatterBomb abm ? abm.manualFuse
                        : b is ExplosiveBlock keg && keg.manualFuse;
                    float remaining = b is AntimatterBomb ab ? ab.fuse : ((ExplosiveBlock)b).fuse;
                    var lbl = _labels[li++];

                    // WorldToScreenPoint returns screen pixels with a bottom-left origin;
                    // the label is positioned in panel design units. Converting through the
                    // shared scale law keeps the countdown on the bomb at every resolution
                    // instead of drifting off it whenever the scale is not exactly 1.
                    Vector2 panelPos = UiScaleSystem.UnityScreenToPanel(sp);
                    lbl.style.left = panelPos.x;
                    lbl.style.top  = panelPos.y;
                    lbl.style.display = DisplayStyle.Flex;

                    if (manual || float.IsInfinity(remaining))
                    {
                        lbl.text = "MAN";
                        lbl.style.color = new Color(1f, 0.62f, 0.18f);
                        continue;
                    }

                    lbl.text = Mathf.Max(0, Mathf.CeilToInt(remaining)) + "s";
                    float denom = b is AntimatterBomb ? 8f : 5f;
                    float frac = Mathf.Clamp01(remaining / Mathf.Max(1f, denom));
                    lbl.style.color = Color.Lerp(new Color(1f, 0.2f, 0.1f), new Color(0.4f, 0.95f, 0.4f), frac);
                }
            }
            for (int i = li; i < MAX_LABELS; i++) _labels[i].style.display = DisplayStyle.None;
        }
    }
}
