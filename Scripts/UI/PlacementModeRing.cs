// Assets/Scripts/VoxelEngine/UI/PlacementModeRing.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        PLACEMENT RING — the second face of the build wheel       ║
// ║                                                                  ║
// ║  Hold the build wheel, then Ctrl + scroll and the wheel turns    ║
// ║  over: the item's own variants step aside and the PLACEMENT ring  ║
// ║  comes up - SINGLE, SPAN, PILLAR, DECK. Another Ctrl + scroll     ║
// ║  and it turns again to the lattice STEP (1 m, 0.5 m, FREE). One   ║
// ║  more and it is back to the item.                                ║
// ║                                                                  ║
// ║  It is a ring, not a menu, because placement is a MODE you carry  ║
// ║  while you build - picking it must cost one flick, not a trip    ║
// ║  through a settings screen.                                      ║
// ║                                                                  ║
// ║  Each wheel owns its own ring instance (the player rig carries    ║
// ║  several wheels at once), so the ring holds no static state and   ║
// ║  can never caption the wrong dial.                               ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.UI
{
    /// <summary>One wheel's placement face: pattern and lattice step, reachable with Ctrl + scroll.</summary>
    public sealed class PlacementModeRing
    {
        public enum Stage
        {
            Base = 0,      // the owning wheel's own options
            Pattern = 1,   // SINGLE / SPAN / PILLAR / DECK
            Step = 2,      // 1 m / 0.5 m / FREE
        }

        private readonly RadialWheelController _wheel;
        private readonly string _baseLabel;
        private readonly string _baseSwapsHint;
        private readonly bool _home;

        private Stage _stage = Stage.Base;

        /// <param name="wheel">The dial this ring lives inside.</param>
        /// <param name="baseLabel">The caption the wheel normally shows ("CONVEYOR SHAPE").</param>
        /// <param name="home">
        /// True for a wheel whose whole job is placement (static blocks have no shape
        /// variants of their own), so the ring starts on PATTERN and never shows an
        /// empty base face.
        /// </param>
        public PlacementModeRing(RadialWheelController wheel, string baseLabel, bool home)
        {
            _wheel = wheel;
            _baseLabel = baseLabel;
            _baseSwapsHint = wheel != null ? wheel.SwapHint : string.Empty;
            _home = home;
            if (_wheel == null) return;

            _wheel.CtrlSwapGroup = Advance;
            _wheel.CtrlSwapHint = home ? "LATTICE STEP" : "PLACEMENT MODES";
            if (home) _stage = Stage.Pattern;
        }

        /// <summary>True while the ring is showing its own options.</summary>
        public bool ShowingOwnOptions => Stage.Pattern == _stage || Stage.Step == _stage;

        /// <summary>
        /// Call once per frame from the owning wheel's Update, immediately BEFORE
        /// <c>_wheel.Tick(...)</c>: it restores the captions after the owner wrote its
        /// own, and clears the ring when the dial closes.
        /// </summary>
        public void Tick()
        {
            if (_wheel == null) return;

            if (!_wheel.IsOpen && !_home) _stage = Stage.Base;
            if (!_wheel.IsOpen && _home) _stage = Stage.Pattern;

            switch (_stage)
            {
                case Stage.Pattern:
                    _wheel.GroupLabel = "BUILD PATTERN";
                    _wheel.SwapHint = string.Empty;
                    _wheel.CtrlSwapHint = "LATTICE STEP";
                    _wheel.IdleTitle = "KEEP PATTERN";
                    _wheel.IdleIcon = PlacementPatternIcons.For(PlacementOptions.Pattern);
                    break;

                case Stage.Step:
                    _wheel.GroupLabel = "LATTICE STEP";
                    _wheel.SwapHint = string.Empty;
                    _wheel.CtrlSwapHint = _home ? "BACK TO PATTERNS" : "BACK TO " + (_baseLabel ?? string.Empty);
                    _wheel.IdleTitle = "KEEP STEP";
                    _wheel.IdleIcon = PlacementPatternIcons.ForStep(PlacementOptions.Step);
                    break;

                default:
                    _wheel.GroupLabel = _baseLabel;
                    _wheel.SwapHint = _baseSwapsHint;
                    _wheel.CtrlSwapHint = "PLACEMENT MODES";
                    if (_home) _wheel.IdleIcon = PlacementPatternIcons.For(PlacementOptions.Pattern);
                    break;
            }
        }

        private void Advance()
        {
            if (_wheel == null) return;
            _stage = _stage switch
            {
                Stage.Pattern => Stage.Step,
                Stage.Step => _home ? Stage.Pattern : Stage.Base,
                _ => Stage.Pattern,
            };
            Tick();
            _wheel.Invalidate();
        }

        /// <summary>Fills the option list when the ring owns the dial. False leaves it to the owner.</summary>
        public bool TryFill(List<RadialOption> options)
        {
            if (options == null) return false;

            if (_stage == Stage.Pattern)
            {
                var current = PlacementOptions.Pattern;
                for (int i = 0; i < 4; i++)
                {
                    var pattern = PlacementOptions.PatternAt(i);
                    options.Add(new RadialOption
                    {
                        Title = PlacementOptions.PatternName(pattern),
                        Description = PlacementOptions.PatternBlurb(pattern),
                        Icon = PlacementPatternIcons.For(pattern),
                        Available = true,
                        Affordable = true,
                        Selected = pattern == current,
                    });
                }
                return true;
            }

            if (_stage == Stage.Step)
            {
                var current = PlacementOptions.Step;
                for (int i = 0; i < 3; i++)
                {
                    var step = PlacementOptions.StepAt(i);
                    options.Add(new RadialOption
                    {
                        Title = PlacementOptions.StepName(step),
                        Description = PlacementOptions.StepBlurb(step),
                        Icon = PlacementPatternIcons.ForStep(step),
                        Available = true,
                        Affordable = true,
                        Selected = step == current,
                    });
                }
                return true;
            }

            return false;
        }

        /// <summary>Applies a confirmed wedge when the ring owns the dial.</summary>
        public bool TryConfirm(int index)
        {
            if (index < 0) return false;

            if (_stage == Stage.Pattern)
            {
                if (index > 3) return true;
                var pattern = PlacementOptions.PatternAt(index);
                PlacementOptions.SetPattern(pattern);
                BuildFeedbackHud.Show("Build Pattern", PlacementOptions.PatternName(pattern), null,
                    UITheme.AccentCyan);
                VoxelEngine.FX.AudioManager.PlayUI(
                    VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.UiClick), 0.55f,
                    pattern == PlacementPattern.Single ? 1.02f : 1.16f);
                return true;
            }

            if (_stage == Stage.Step)
            {
                if (index > 2) return true;
                var step = PlacementOptions.StepAt(index);
                PlacementOptions.SetStep(step);
                BuildFeedbackHud.Show("Lattice Step", PlacementOptions.StepName(step), null,
                    UITheme.AccentGold);
                return true;
            }

            return false;
        }

        /// <summary>True when the held item may lay runs at all.</summary>
        public static bool ItemSupportsRuns(VoxelEngine.Items.BlockItem block)
            => PlacementOptions.IsPatternEligible(block);

        /// <summary>A one-line caption for the prompt pill under the crosshair.</summary>
        public static string PromptTail()
        {
            string pattern = PlacementOptions.PatternName(PlacementOptions.Pattern);
            if (PlacementOptions.Pattern == PlacementPattern.Single) return pattern;
            return pattern + " · " + PlacementOptions.StepName(PlacementOptions.Step);
        }

        /// <summary>The Ctrl hint, spelled with whatever key the player actually uses.</summary>
        public static string CtrlHint()
            => "CTRL+SCROLL  PLACEMENT";

    }
}
