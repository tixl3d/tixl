using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

/// <summary>
/// Incomplete adaptation of the Apc40 controller for Tooll.
/// 
/// Attempt of implementing advanced logic for Apc40MK2 controller. Sadly advanced
/// features like receiving button combinations or setting the LED-lights requires the
/// device to be in "Ableton with full ctrl mode". So far, all attempts of activating
/// this mode by send midi-system messages have been unsuccessful.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "UnusedMember.Local")]
[MidiDeviceProduct("Akai APC40")]
public sealed class Apc40Mk1 : CompatibleMidiDevice
{
    public Apc40Mk1()
    {
        CommandTriggerCombinations
            = new List<CommandTriggerCombination>()
                  {
                      new(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      new(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      new(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      //new CommandTriggerCombination(SnapshotActions.ActivateGroupAtIndex, InputModes.Default, new[] { ClipStopButtons1To8 }, CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed ),
                      new(BlendActions.StopBlendingTowards, InputModes.Default, new[] { SceneLaunch2 },
                          CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                      new(BlendActions.StartBlendingTowardsSnapshot, requiredInputMode: InputModes.BlendTo, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      new(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, new[] { AbFader },
                          CommandTriggerCombination.ExecutesAt.ControllerChange),
                  };

        ModeButtons = new List<ModeButton>
                          {
                              new(SceneLaunch2, InputModes.BlendTo),
                              new(SceneLaunch1, InputModes.Delete),
                          };
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;
        if (!_initialized)
        {
            // Should be: F0 47 00 73 60 00 04 xx 08 01 01 F7
            Log.Debug("Sending init SysEx message to APC40 Mk1...");
            var buffer = new byte[]
                             {
                                 0xF0, // MIDI exclusive start
                                 0x47, // Manufacturers ID Byte
                                 0x00, // System Exclusive Device ID
                                 0x73, // Product model ID
                                 0x60, // Message type identifier
                                 0x00, // Number of data bytes to follow (most significant)
                                 0x04, // Number of data bytes to follow (least significant)
                                 0x40, // Application/Configuration Identifier (40 for Generic, 41 for Ableton Live Mode, 42 for Alternate Ableton Live Mode)
                                 0x08, // PC application Software version major (Set to 8.1.1 but doesn't matter)
                                 0x01, // PC application Software version minor (Set to 8.1.1 but doesn't matter)
                                 0x01, // PC application Software bug-fix level (Set to 8.1.1 but doesn't matter)
                                 0xF7 // MIDI exclusive end
                             };
            MidiOutConnection?.SendBuffer(buffer);
            _initialized = true;
        }

        UpdateRangeLeds(SceneTrigger1To40,
                        mappedIndex =>
                        {
                            var color = Apc40Mk1Colors.Off;
                            if (SymbolVariationPool.TryGetSnapshot(mappedIndex, out var v))
                            {
                                color = v.State switch
                                            {
                                                Variation.States.Undefined => Apc40Mk1Colors.Off,
                                                Variation.States.InActive  => Apc40Mk1Colors.Green,
                                                Variation.States.Active    => Apc40Mk1Colors.Red,
                                                Variation.States.Modified  => Apc40Mk1Colors.Orange,
                                                _                          => color
                                            };
                            }

                            return AddModeHighlight(mappedIndex, (int)color);
                        });

        // Update groups
        /* UpdateRangeLeds(midiOut, ClipStopButtons1To8,
                         mappedIndex =>
                         {
                             var g = activeVariation.GetGroupAtIndex(mappedIndex);
                             var isUndefined = g == null;
                             var color1 = isUndefined
                                              ? Apc40Mk1Colors.Off
                                              : g.Id == activeVariation.ActiveGroupId
                                                  ? Apc40Mk1Colors.Red
                                                  : Apc40Mk1Colors.Off;
                             return (int)color1;
                         });*/

         /*UpdateRangeLeds(midiOut, SceneLaunch1To5,
                         mappedIndex =>
                         {
                             var g1 = activeVariation.GetGroupAtIndex(mappedIndex);
                             var isUndefined1 = g1 == null;
                             var color2 = isUndefined1
                                              ? Apc40Mk1Colors.Off
                                              : g1.Id == activeVariation.ActiveGroupId
                                                  ? Apc40Mk1Colors.Red
                                                  : Apc40Mk1Colors.Off;
                             return (int)color2;
                         });*/
    }

    private int AddModeHighlight(int index, int orgColor)
    {
        var indicatedStatus = (_updateCount + index / 8) % 30 < 4;
        if (!indicatedStatus)
        {
            return orgColor;
        }

        if (ActiveMode == InputModes.BlendTo)
        {
            return (int)Apc40Mk1Colors.Orange;
        }
        else if (ActiveMode == InputModes.Delete)
        {
            return (int)Apc40Mk1Colors.Red;
        }

        return orgColor;
    }

    private int _updateCount;

    // Buttons
    private static readonly ButtonRange SceneTrigger1To40 = new(0, 0 + 40);
    private static readonly ButtonRange SceneLaunch1To5 = new(82, 82 + 5);
    private static readonly ButtonRange SceneLaunch1 = new(82);
    private static readonly ButtonRange SceneLaunch2 = new(82 + 1);
    private static readonly ButtonRange ClipStopButtons1To8 = new(52, 52 + 8);
    private static readonly ButtonRange ClipABButtons1To8 = new(66, 66 + 8);
    private static readonly ButtonRange ClipNumberButtons1To8 = new(50, 50 + 8);
    private static readonly ButtonRange ClipSButtons1To8 = new(49, 49 + 8);
    private static readonly ButtonRange ClipRectButtons1To8 = new(49, 49 + 8);
    private static readonly ButtonRange BankSelectTop = new(94);
    private static readonly ButtonRange BankSelectRight = new(96);
    private static readonly ButtonRange BankSelectBottom = new(95);
    private static readonly ButtonRange Shift = new(98);
    private static readonly ButtonRange Bank = new(103);
    private static readonly ButtonRange DetailView = new(65);
    private static readonly ButtonRange ClipDevView = new(64);
    private static readonly ButtonRange DevLock = new(63);
    private static readonly ButtonRange DevOnOff = new(62);
    private static readonly ButtonRange BankRightArrow = new(61);
    private static readonly ButtonRange BankLeftArrow = new(60);
    private static readonly ButtonRange DeviceRightArrow = new(59);
    private static readonly ButtonRange DeviceLeftArrow = new(58);
    private static readonly ButtonRange NudgePlus = new(101);
    private static readonly ButtonRange NudgeNegative = new(100);
    private static readonly ButtonRange User = new(89);
    private static readonly ButtonRange TapTempo = new(99);
    private static readonly ButtonRange Metronome = new(90);
    private static readonly ButtonRange Sends = new(88);
    private static readonly ButtonRange Pan = new(87);
    private static readonly ButtonRange Play = new(91);
    private static readonly ButtonRange Record = new(93);
    private static readonly ButtonRange Session = new(102);

    // Knobs
    //private static readonly ButtonRange Sliders1To9 = new ButtonRange(48, 48 + 8);
    private static readonly ButtonRange Fader1To8 = new(0, 7);
    private static readonly ButtonRange RightPerBankKnobs = new(16, 16 + 7);
    private static readonly ButtonRange MasterFader = new(14);
    private static readonly ButtonRange AbFader = new(15);
    private static readonly ButtonRange TopKnobs1To8 = new(48, 48 + 7);
    private static readonly ButtonRange CueLevelKnob = new(47);
    private static readonly ButtonRange TempoKnob = new(13);

    /// <summary>
    /// This is sub set of the original colors defined in reference
    /// </summary>
    /// <remarks>
    /// Also see
    /// - https://www.akaipro.de/sites/default/files/2018-01/APC40Mk2_Communications_Protocol_v1.2.pdf_7db83a06354c396174676105098e3a7d.pdf
    /// - https://docs.google.com/spreadsheets/d/1pCAOFYCUAilqwpT1rA5_pxA1u19Yipcy_RI4egxKy5k/edit?usp=sharing
    /// </remarks>
    private enum Apc40Mk1Colors
    {
        Off = 0,
        Green = 1,
        GreenBlinking = 2,
        Red = 3,
        RedBlinking = 4,
        Orange = 5,
        OrangeBlinking = 6,
        
    };

    private bool _initialized;
}