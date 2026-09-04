#nullable enable
using System.Diagnostics.CodeAnalysis;
using NAudio;
using NAudio.Midi;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

[SuppressMessage("ReSharper", "UnusedMember.Local")]
[MidiDeviceProduct("APC mini mk2")]
public sealed class ApcMiniMk2 : CompatibleMidiDevice
{
    public ApcMiniMk2()
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

                      new(BlendActions.StartBlendingTowardsSnapshot, InputModes.BlendTo, new[] { SceneTrigger1To64 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                      new(BlendActions.StopBlendingTowards, InputModes.Default, new[] { SceneLaunch8ClipStopAll },
                          CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),

                      new(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, new[] { Slider9 },
                          CommandTriggerCombination.ExecutesAt.ControllerChange),

                      new(BlendActions.UpdateBlendValues, InputModes.Default, new[] { Sliders1To8 },
                          CommandTriggerCombination.ExecutesAt.ControllerChange),
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
                            var color = Colors.Off;
                            if (!SymbolVariationPool.TryGetSnapshot(mappedIndex, out var variation))
                            {
                                return (int)color;
                            }

                            if (variation.State == Variation.States.Active)
                            {
                                return (int)Colors.Red;
                            }

                            switch (variation.State)
                            {
                                case Variation.States.Undefined:
                                    color = Colors.Off;
                                    break;
                                case Variation.States.InActive:
                                    color = Colors.Green;
                                    break;
                                case Variation.States.Active:
                                    color = Colors.Red;
                                    break;
                                case Variation.States.Modified:
                                    color = Colors.Orange;
                                    break;
                                case Variation.States.IsBlended:
                                    color = Colors.BrightPurpleBlue;
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
                       InputModes.Save    => (int)Colors.Yellow,
                       InputModes.BlendTo => (int)Colors.Yellow,
                       InputModes.Delete  => (int)Colors.Red,
                       _                  => orgColor
                   };
    }

    private int _updateCount;

    protected override void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        if (CacheControllerColors[apcControlIndex] == colorCode)
            return;

        //const int  = 150;
        var noteOnEvent = new NoteOnEvent(0, (int)LedStatesMidiChannels.On100, apcControlIndex, colorCode, 50);
        try
        {
            midiOut.Send(noteOnEvent.GetAsShortMessage());
        }
        catch (MmException e)
        {
            Log.Warning("Failed setting midi color message:" + e.Message);
        }

        CacheControllerColors[apcControlIndex] = colorCode;
    }

    private enum LedStatesBytes
    {
        On010 = 144,
        On025 = 145,
        On050 = 146,
        On065 = 147,
        On075 = 148,
        On090 = 149,
        On100 = 150,
        Pulsing1By16 = 151,
        Pulsing1By8 = 152,
        Pulsing1By4 = 153,
        Pulsing1By2 = 154,
        Blinking1By24 = 154,
        Blinking1By16 = 155,
        Blinking1By8 = 156,
        Blinking1By4 = 157,
        Blinking1By2 = 158,
    }
    
    private enum LedStatesMidiChannels
    {
        On010 = 0,
        On025 = 1,
        On050 = 2,
        On065 = 3,
        On075 = 4,
        On090 = 5,
        On100 = 6,
        Pulsing1By16 = 7,
        Pulsing1By8 = 8,
        Pulsing1By4 = 9,
        Pulsing1By2 = 10,
        Blinking1By24 = 11,
        Blinking1By16 = 12,
        Blinking1By8 = 13,
        Blinking1By4 = 14,
        Blinking1By2 = 15,
    }

    private enum Colors
    {
        Off = 0, //000000
        Charcoal = 1, //1E1E1E
        Gray = 2, //7F7F7F
        White = 3, //FFFFFF
        RedOrange = 4, //FF4C4C
        Red = 5, //FF0000
        DarkRed = 6, //590000
        VeryDarkRed = 7, //190000
        Peach = 8, //FFBD6C
        Orange = 9, //FF5400
        BurntOrange = 10, //591D00
        DarkBrown = 11, //271B00
        LemonYellow = 12, //FFFF4C
        Yellow = 13, //FFFF00
        Olive = 14, //595900
        DarkOlive = 15, //191900
        LightLime = 16, //88FF4C
        LimeGreen = 17, //54FF00
        DarkLimeGreen = 18, //1D5900
        VeryDarkLimeGreen = 19, //142B00
        BrightGreen = 20, //4CFF4C
        Green = 21, //00FF00
        DarkGreen = 22, //005900
        VeryDarkGreen = 23, //001900
        MintGreen = 24, //4CFF5E
        BrightMintGreen = 25, //00FF19
        ForestGreen = 26, //00590D
        VeryDarkForestGreen = 27, //001902
        LightAquamarine = 28, //4CFF88
        Aquamarine = 29, //00FF55
        DarkAquamarine = 30, //00591D
        VeryDarkAquamarine = 31, //001F12
        LightSeaGreen = 32, //4CFFB7
        SeaGreen = 33, //00FF99
        DarkSeaGreen = 34, //005935
        VeryDarkSeaGreen = 35, //001912
        LightSkyBlue = 36, //4CC3FF
        SkyBlue = 37, //00A9FF
        DarkSkyBlue = 38, //004152
        VeryDarkSkyBlue = 39, //001019
        LightAzure = 40, //4C88FF
        Azure = 41, //0055FF
        MidnightBlue = 42, //001D59
        DarkNavyBlue = 43, //000819
        LightRoyalBlue = 44, //4C4CFF
        Blue = 45, //0000FF
        NavyBlue = 46, //000059
        VeryDarkNavyBlue = 47, //000019
        LightViolet = 48, //874CFF
        Violet = 49, //5400FF
        DarkViolet = 50, //190064
        VeryDarkViolet = 51, //0F0030
        LightMagenta = 52, //FF4CFF
        Magenta = 53, //FF00FF
        DarkMagenta = 54, //590059
        VeryDarkMagenta = 55, //190019
        LightPink = 56, //FF4C87
        Pink = 57, //FF0054
        DarkPink = 58, //59001D
        VeryDarkPink = 59, //220013
        BrightRed = 60, //FF1500
        Chestnut = 61, //993500
        DarkChestnut = 62, //795100
        ForestGreenBrown = 63, //436400
        VeryDarkGreenBrown = 64, //033900
        DarkTealGreen = 65, //005735
        DarkCeruleanBlue = 66, //00547F
        ElectricBlue = 67, //0000FF
        DarkCyanBlue = 68, //00454F
        CobaltBlue = 69, //2500CC
        MediumGray = 70, //7F7F7F
        DarkGray = 71, //202020
        BrightLimeYellow = 72, //BDFF2D
        ChartreuseYellow = 73, //AFED06
        NeonGreenYellow = 74, //64FF09
        DarkForestGreen = 75, //108B00
        TurquoiseGreen = 76, //00FF87
        SkyBlueGreen = 77, //00A9FF
        SapphireBlue = 78, //002AFF
        BrightPurpleBlue = 79, //3F00FF
        ElectricPurpleBlue = 80, //7A00FF
        DeepMagentaPink = 81, //B21A7D
        DarkCoffeeBrown = 82, //402100
        BrightOrangeRed = 83, //FF4A00
        LimeGreenYellow = 84, //88E106
        BrightLimeGreenYellow = 85, //72FF15
        BrightGreenYellow = 86, //00FF00
        LightGreenYellow = 87, //3BFF26
        MintGreenYellow = 88, //59FF71
        LightTurquoiseGreen = 89, //38FFCC
        BrightBlueGreen = 90, //5B8AFF
        LightBlueGreen = 91, //3151C6
        LightLavenderBlue = 92, //877FE9
        BrightMagentaPink = 93, //D31DFF
        MagentaPinkRed = 94, //FF005D
        BrightOrangeYellow = 95, //FF7F00
        YellowOliveGreen = 96, //B9B000
        NeonGreenYellow2 = 97, //90FF00
        BrownYellow = 98, //835D07
        DarkOliveYellow = 99, //392B00
        VeryDarkGreenYellow = 100, //144C10
        DarkTealGreenBlue = 101, //0D5038
        DarkNavyBlueGray = 102, //15152A
        DarkBlueGray = 103, //16205A
        VeryDarkChestnut = 104, //693C1C
        CrimsonRed = 105, //A8000A
        LightRedOrange = 106, //DE513D
        BurntOrangeRed = 107, //D86A1C
        YellowGold = 108, //FFE126
        LimeGreenGold = 109, //9EE12F
        DarkLimeGreenGold = 110, //67B50F
        CharcoalGray = 111, //1E1E30
        LightYellowGreen = 112, //DCFF6B
        LightTurquoiseBlue = 113, //80FFBD
        LightLavenderPurple = 114, //9A99FF
        LightPurpleBlue = 115, //8E66FF
        MediumGrayBlue = 116, //404040
        MediumGrayBrown = 117, //757575
        LightCyanBlue = 118, //E0FFFF
        CrimsonRedDark = 119, //A00000
        DarkRedBrown = 120, //350000
        NeonGreenDark = 121, //1AD000
        ForestGreenDark = 122, //074200
        YellowOliveBrown = 123, //B9B000
        OliveBrownYellow = 124, //3F3100
        OliveBrown = 125, //3F3100
        BurntOrange2 = 126, //B35F00
        DarkRed2 = 127, //4B1502
    }

    // Same bottom-up 8×8 grid as the APC Mini (note 0 = bottom-left); flip rows so the canonical
    // snapshot index 0 lands on the physical top-left pad, for both LED output and button input.
    private static readonly ButtonRange SceneTrigger1To64 = new(0, 63, mapToIndex: position => (8 - 1 - position / 8) * 8 + position % 8);
    private static readonly ButtonRange Sliders1To9 = new(48, 48 + 8);
    private static readonly ButtonRange Sliders1To8 = new(48, 48 + 7);
    private static readonly ButtonRange Slider9 = new(48 + 8, 48 + 8);

    private static readonly ButtonRange TrackButtons1To8 = new(100, 107);

    private static readonly ButtonRange TrackButton1ButtonVolume = new(100);
    private static readonly ButtonRange TrackButton2ButtonPan = new(101);
    private static readonly ButtonRange TrackButton3ButtonSend = new(102);
    private static readonly ButtonRange TrackButton4ButtonDevice = new(103);
    private static readonly ButtonRange TrackButton5ButtonUp = new(104);
    private static readonly ButtonRange TrackButton6ButtonDown = new(105);
    private static readonly ButtonRange TrackButton7ButtonLeft = new(106);
    private static readonly ButtonRange TrackButton8ButtonRight = new(107);

    private static readonly ButtonRange SceneLaunch1To8 = new(112, 119);

    private static readonly ButtonRange SceneLaunch1ClipStop = new(112);
    private static readonly ButtonRange SceneLaunch2Solo = new(113);
    private static readonly ButtonRange SceneLaunch3Mute = new(114);
    private static readonly ButtonRange SceneLaunch4RecArm = new(115);
    private static readonly ButtonRange SceneLaunch5Select = new(116);
    private static readonly ButtonRange SceneLaunch6Drum = new(117);
    private static readonly ButtonRange SceneLaunch7Note = new(118);
    private static readonly ButtonRange SceneLaunch8ClipStopAll = new(119);

    private static readonly ButtonRange Shift = new(123);
}