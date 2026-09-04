#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

[SuppressMessage("ReSharper", "UnusedMember.Local")]

[MidiDeviceProduct("APC MINI")]
public sealed class ApcMini : CompatibleMidiDevice
{
    public ApcMini()
    {
        CommandTriggerCombinations
            = new List<CommandTriggerCombination>
                  {
                      new(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, new[] { SceneTrigger1To64 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      new(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, new[] { SceneTrigger1To64 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                      new(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, new[] { SceneTrigger1To64 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                      new(BlendActions.StartBlendingSnapshots, InputModes.Default, new[] { SceneTrigger1To64 },
                          CommandTriggerCombination.ExecutesAt.AllCombinedButtonsReleased),

                      
                      new(BlendActions.StartBlendingTowardsSnapshot, 
                          requiredInputMode: InputModes.BlendTo, 
                          new[] { SceneTrigger1To64 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                      // Sadly this is not triggered. 
                      // new(BlendActions.StopBlendingTowards, InputModes.BlendTo, new[] { Shift },
                      //     CommandTriggerCombination.ExecutesAt.ModeButtonReleased),
                      
                      new(BlendActions.StopBlendingTowards, InputModes.Default, new[] { SceneLaunch8ClipStopAll },
                          CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                      
                      new(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, new[] { Slider9 },
                          CommandTriggerCombination.ExecutesAt.ControllerChange),
                      
                      new(BlendActions.UpdateBlendValues, InputModes.Default, new[] { Sliders1To8 }, 
                          CommandTriggerCombination.ExecutesAt.ControllerChange),

                      // A first stub for parameter collection control.
                      // new(ParamCollectionActions.SetParamGroupControl, InputModes.Default, new[] { Sliders1To8 },
                      //     CommandTriggerCombination.ExecutesAt.ControllerChange),
                      
                      // new(SnapshotActions.SaveSnapshotAtNextFreeSlot, InputModes.Default, new[] { SceneLaunch8ClipStopAll },
                      //     CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                      
                      //new CommandTriggerCombination(VariationHandling.ActivateGroupAtIndex, InputModes.Default, new[] { ChannelButtons1To8 }, CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                  };
        
        ModeButtons = new List<ModeButton>
                          {
                              new(Shift, InputModes.BlendTo),
                              new(SceneLaunch1ClipStop, InputModes.Delete),
                          };
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;

        UpdateRangeLeds(SceneTrigger1To64,
                        mappedIndex =>
                        {
                            var color = ApcButtonColor.Off;
                            if (!SymbolVariationPool.TryGetSnapshot(mappedIndex, out var variation))
                            {
                                return (int)color;
                            }

                            if (variation.State == Variation.States.Active)
                            {
                                return (int)ApcButtonColor.Red;
                            }

                            switch (variation.State)
                            {
                                case Variation.States.Undefined:
                                    color = ApcButtonColor.Off;
                                    break;
                                case Variation.States.InActive:
                                    color = ApcButtonColor.Green;
                                    break;
                                case Variation.States.Active:
                                    color = ApcButtonColor.Red;
                                    break;
                                case Variation.States.Modified:
                                    color = ApcButtonColor.YellowBlinking;
                                    break;
                                case Variation.States.IsBlended:
                                    color = ApcButtonColor.RedBlinking;
                                    break;
                            }

                            return AddModeHighlight(mappedIndex, (int)color);
                        });
    }

    private int AddModeHighlight(int index, int orgColor)
    {
        var indicatedStatus = (_updateCount + index / 8) % 30 < 4;
        if (!indicatedStatus)
        {
            return orgColor;
        }

        return ActiveMode switch
                   {
                       InputModes.Save   => (int)ApcButtonColor.Yellow,
                       InputModes.BlendTo => (int)ApcButtonColor.Yellow,
                       InputModes.Delete => (int)ApcButtonColor.Red,
                       _                 => orgColor
                   };
    }

    private int _updateCount;

    private enum ApcButtonColor
    {
        Undefined = -1,
        Off,
        Green,
        GreenBlinking,
        Red,
        RedBlinking,
        Yellow,
        YellowBlinking,
    }

    // The APC Mini numbers its clip pads row-major from the bottom (note 0 = bottom-left), but the
    // snapshot index is canonical: 0 = top-left, reading order. Flip rows so the top-left pad is index 0.
    // The transform feeds both LED output and button input, keeping lights and presses in sync.
    private static readonly ButtonRange SceneTrigger1To64 = new(0, 63, mapToIndex: position => (8 - 1 - position / 8) * 8 + position % 8);
    private static readonly ButtonRange Sliders1To9 = new(48, 48 + 8);
    private static readonly ButtonRange Sliders1To8 = new(48, 48 + 7);
    private static readonly ButtonRange Slider9 = new(48 + 8, 48 + 8);

    private static readonly ButtonRange ChannelButtons1To8 = new(64, 71);
    private static readonly ButtonRange ButtonUp = new(64);
    private static readonly ButtonRange ButtonDown = new(65);
    private static readonly ButtonRange ButtonLeft = new(66);
    private static readonly ButtonRange ButtonRight = new(67);
    private static readonly ButtonRange ButtonVolume = new(68);
    private static readonly ButtonRange ButtonPan = new(69);
    private static readonly ButtonRange ButtonSend = new(70);
    private static readonly ButtonRange ButtonDevice = new(71);

    private static readonly ButtonRange SceneLaunch1To8 = new(82, 89);
    private static readonly ButtonRange SceneLaunch1ClipStop = new(82);
    private static readonly ButtonRange SceneLaunch2ClipSolo = new(83);
    private static readonly ButtonRange SceneLaunch3ClipRecArm = new(84);
    private static readonly ButtonRange SceneLaunch4ClipMute = new(85);
    private static readonly ButtonRange SceneLaunch5ClipSelect = new(86);
    private static readonly ButtonRange SceneLaunch8ClipStopAll = new(89);

    private static readonly ButtonRange Shift = new(98);
}