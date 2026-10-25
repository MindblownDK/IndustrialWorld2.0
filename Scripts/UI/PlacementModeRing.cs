// Assets/Scripts/VoxelEngine/UI/PlacementModeRing.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        PLACEMENT RING — the second face of the build wheel       ║
// ║                                                                  ║
// ║  Hold the build wheel, then Ctrl + scroll to turn from the        ║
// ║  item's own variants to the PLACEMENT ring: SINGLE, SPAN, PILLAR, ║
// ║  DECK and WALL. A further turn can show the lattice STEP (1 m,    ║
// ║  0.5 m, FREE), then the ring returns to the item's own options.  ║
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
            Pattern = 1,   // SINGLE / SPAN / PILLAR / DECK / WALL
            Step = 2,      // 1 m / 0.5 m / FREE
            Seat = 3,      // LEVEL / FOLLOW GROUND - how a run sits on the ground
        }

        private readonly RadialWheelController _wheel;
        private readonly string _baseLabel;
        private readonly string _baseSwapsHint;
        private readonly string _baseIdleTitle;
        private readonly string _baseIdleDescription;
        private readonly Texture2D _baseIdleIcon;
        private readonly Color _baseIdleTitleColor;
        private readonly bool _home;
        private readonly bool _includeStep;
        private readonly bool _patternOnly;

        private Stage _stage = Stage.Base;

        /// <param name="wheel">The dial this ring lives inside.</param>
        /// <param name="baseLabel">The caption the wheel normally shows ("CONVEYOR SHAPE").</param>
        /// <param name="home">
        /// True for a wheel whose whole job is placement (static blocks have no shape
        /// variants of their own), so the ring starts on PATTERN and never shows an
        /// empty base face.
        /// </param>
        /// <param name="includeStep">Whether the pattern face may turn over to a lattice-step face.</param>
        /// <param name="patternOnly">Locks a placement-only wheel to its pattern face, with no empty base face.</param>
        public PlacementModeRing(RadialWheelController wheel, string baseLabel, bool home,
            bool includeStep = true, bool patternOnly = false)
        {
            _wheel = wheel;
            _baseLabel = baseLabel;
            _baseSwapsHint = wheel != null ? wheel.SwapHint : string.Empty;
            _baseIdleTitle = wheel != null ? wheel.IdleTitle : string.Empty;
            _baseIdleDescription = wheel != null ? wheel.IdleDescription : string.Empty;
            _baseIdleIcon = wheel != null ? wheel.IdleIcon : null;
            _baseIdleTitleColor = wheel != null ? wheel.IdleTitleColor : Color.white;
            _home = home || patternOnly;
            _includeStep = includeStep;
            _patternOnly = patternOnly;
            if (_wheel == null) return;

            _wheel.CtrlSwapGroup = Advance;
            _wheel.CtrlSwapHint = patternOnly ? string.Empty
                : home ? "LATTICE STEP" : "PLACEMENT MODES";
            if (_home) _stage = Stage.Pattern;
        }

        /// <summary>True while the ring is showing its own options.</summary>
        public bool ShowingOwnOptions => Stage.Pattern == _stage || Stage.Step == _stage
            || Stage.Seat == _stage;

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
                    _wheel.CtrlSwapHint = _patternOnly ? string.Empty
                        : _includeStep ? "LATTICE STEP"
                        : "BACK TO " + (_baseLabel ?? string.Empty);
                    _wheel.IdleTitle = "KEEP PATTERN";
                    _wheel.IdleDescription = "Release in the centre to keep the selected build pattern";
                    _wheel.IdleIcon = PlacementPatternIcons.For(PlacementOptions.Pattern);
                    break;

                case Stage.Step:
                    _wheel.GroupLabel = "LATTICE STEP";
                    _wheel.SwapHint = string.Empty;
                    _wheel.CtrlSwapHint = _home ? "RUN SEATING" : "BACK TO " + (_baseLabel ?? string.Empty);
                    _wheel.IdleTitle = "KEEP STEP";
                    _wheel.IdleDescription = "Release in the centre to keep the current lattice step";
                    _wheel.IdleIcon = PlacementPatternIcons.ForStep(PlacementOptions.Step);
                    break;

                case Stage.Seat:
                    _wheel.GroupLabel = "RUN SEATING";
                    _wheel.SwapHint = string.Empty;
                    _wheel.CtrlSwapHint = "BACK TO PATTERNS";
                    _wheel.IdleTitle = "KEEP SEATING";
                    _wheel.IdleDescription = "Release in the centre to keep the current run seating";
                    _wheel.IdleIcon = PlacementPatternIcons.For(PlacementOptions.Pattern);
                    break;


                default:
                    _wheel.GroupLabel = _baseLabel;
                    _wheel.SwapHint = _baseSwapsHint;
                    _wheel.CtrlSwapHint = "PLACEMENT MODES";
                    _wheel.IdleTitle = _baseIdleTitle;
                    _wheel.IdleDescription = _baseIdleDescription;
                    _wheel.IdleIcon = _home ? PlacementPatternIcons.For(PlacementOptions.Pattern) : _baseIdleIcon;
                    _wheel.IdleTitleColor = _baseIdleTitleColor;
                    break;
            }
        }

        private void Advance()
        {
            if (_wheel == null) return;
            if (_patternOnly) return;
            _stage = _stage switch
            {
                Stage.Pattern => _includeStep ? Stage.Step : Stage.Base,
                Stage.Step => _home ? Stage.Seat : Stage.Base,
                Stage.Seat => _home ? Stage.Pattern : Stage.Base,
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
                for (int i = 0; i < 5; i++)
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

            if (_stage == Stage.Seat)
            {
                // 17.45.2-dev: LEVEL or FOLLOW GROUND, as its own face of the same dial,
                // so a slope can be built on without changing the pattern to get there.
                var current = PlacementOptions.Seat;
                for (int i = 0; i < 2; i++)
                {
                    var seat = PlacementOptions.SeatAt(i);
                    options.Add(new RadialOption
                    {
                        Title = PlacementOptions.SeatName(seat),
                        Description = PlacementOptions.SeatBlurb(seat),
                        Badge = PlacementOptions.SeatBadge(seat),
                        Icon = null,
                        Available = true,
                        Affordable = true,
                        Selected = seat == current,
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
                if (index > 4) return true;
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

            if (_stage == Stage.Seat)
            {
                if (index > 1) return true;
                var seat = PlacementOptions.SeatAt(index);
                PlacementOptions.SetSeat(seat);
                BuildFeedbackHud.Show("Run Seating", PlacementOptions.SeatName(seat),
                    null, UITheme.AccentGold);
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
            // 17.50.0-dev: the plane is part of the pattern's name, so it is part of the
            // pill too. A player should never have to wonder whether DECK means flat.
            if (PlacementOptions.Pattern == PlacementPattern.Deck) pattern += " · FLAT";
            if (PlacementOptions.Pattern == PlacementPattern.Wall) pattern += " · VERTICAL";
            if (PlacementOptions.Pattern == PlacementPattern.Single) return pattern;
            // 17.45.3-dev: the seating is part of the gesture's state now, so it belongs on
            // the pill with the pattern and the step rather than hidden one wheel face away.
            return pattern + " · " + PlacementOptions.StepName(PlacementOptions.Step)
                + " · " + PlacementOptions.SeatName(PlacementOptions.Seat);
        }

        /// <summary>The Ctrl hint, spelled with whatever key the player actually uses.</summary>
        public static string CtrlHint()
            => "CTRL+SCROLL  PATTERN / STEP / SEATING";

    }
}
