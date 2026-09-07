#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

[SuppressMessage("ReSharper", "UnusedMember.Local")]

[MidiDeviceProduct("X-TOUCH MINI")]
public class XTouchMini : CompatibleMidiDevice
{
    public XTouchMini()
    {
        CommandTriggerCombinations
            = new List<CommandTriggerCombination>
                  {
                      // new(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, new[] { SceneTrigger1To64 },
                      //     CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      // new(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, new[] { SceneTrigger1To64 },
                      //     CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      //
                      // new(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, new[] { SceneTrigger1To64 },
                      //     CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      //
                      // new(BlendActions.StartBlendingSnapshots, InputModes.Default, new[] { SceneTrigger1To64 },
                      //     CommandTriggerCombination.ExecutesAt.AllCombinedButtonsReleased),
                      //
                      //
                      // new(BlendActions.StartBlendingTowardsSnapshot, 
                      //     requiredInputMode: InputModes.BlendTo, 
                      //     new[] { SceneTrigger1To64 },
                      //     CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      //
                      // new(BlendActions.StopBlendingTowards, InputModes.Default, new[] { SceneLaunch8ClipStopAll },
                      //     CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                      //
                      // new(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, new[] { Slider9 },
                      //     CommandTriggerCombination.ExecutesAt.ControllerChange),
                      //
                      // new(BlendActions.UpdateBlendValues, InputModes.Default, new[] { Knobs1To8 }, 
                      //     CommandTriggerCombination.ExecutesAt.ControllerChange),
                  };
        
        ModeButtons = new List<ModeButton>
                          {
                              // new(Shift, InputModes.BlendTo),
                              // new(SceneLaunch1ClipStop, InputModes.Delete),
                          };
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;
        
        // UpdateRangeLeds(SceneTrigger1To64,
        //                 mappedIndex =>
        //                 {
        //                     var color = ApcButtonColor.Off;
        //                     if (!SymbolVariationPool.TryGetSnapshot(mappedIndex, out var variation))
        //                     {
        //                         return (int)color;
        //                     }
        //
        //                     if (variation.State == Variation.States.Active)
        //                     {
        //                         return (int)ApcButtonColor.Red;
        //                     }
        //
        //                     switch (variation.State)
        //                     {
        //                         case Variation.States.Undefined:
        //                             color = ApcButtonColor.Off;
        //                             break;
        //                         case Variation.States.InActive:
        //                             color = ApcButtonColor.Green;
        //                             break;
        //                         case Variation.States.Active:
        //                             color = ApcButtonColor.Red;
        //                             break;
        //                         case Variation.States.Modified:
        //                             color = ApcButtonColor.YellowBlinking;
        //                             break;
        //                         case Variation.States.IsBlended:
        //                             color = ApcButtonColor.RedBlinking;
        //                             break;
        //                     }
        //
        //                     return AddModeHighlight(mappedIndex, (int)color);
        //                 });
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


    
    
    // MC Mode
    // Note these knobs are relative return 1 ... Number of ticks clockswise
    // 65 ... 65 + number of ticks counter clockwise 
    private static readonly ButtonRange Knobs1To8 = new(16, 16 + 7);

    private static readonly ButtonRange KnobPressed1To8 = new(32, 32 + 7);
    
    private static readonly ButtonRange ButtonUp = new(84);
    private static readonly ButtonRange ButtonDown = new(85);
    
    // ⚠ Fader is undefined???
    

    
    private static readonly ButtonRange ChannelButtons1To8 = new(8, 8 + 7);
    
    private static readonly ButtonRange ChannelButtonsFn1To8 = new(16, 16 + 7);
    private static readonly ButtonRange Fader = new(9);
    

    // Standard mode
    // private static readonly ButtonRange Knobs1To8 = new(11, 11 + 7);
    // private static readonly ButtonRange ChannelButtons1To8 = new(8, 8 + 7);
    // private static readonly ButtonRange ChannelButtonsFn1To8 = new(16, 16 + 7);
    // private static readonly ButtonRange Fader = new(9);

}