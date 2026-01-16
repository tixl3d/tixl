using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[MidiDeviceProduct("Launchpad Mini MK3")]
public sealed class LaunchpadMiniMk3 : CompatibleMidiDevice
{
    public LaunchpadMiniMk3()
    {
        CommandTriggerCombinations
            = new List<CommandTriggerCombination>()
                  {
                      // Main 8x8 Grid for snapshots
                      new(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, new[] { Grid8x8 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      new(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, new[] { Grid8x8 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      new(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, new[] { Grid8x8 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      
                      // Use the right-side "Scene Launch" buttons for mode switching
                      new(BlendActions.StopBlendingTowards, InputModes.Default, new[] { SceneLaunchStop },
                          CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                  };

        ModeButtons = new List<ModeButton>
                          {
                              new(SceneLaunchModeBlend, InputModes.BlendTo),
                              new(SceneLaunchModeDelete, InputModes.Delete),
                          };
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;
        if (!_initialized)
        {
            // Switch Launchpad Mini MK3 to Programmer Mode
            // F0 00 20 29 02 0D 0E 01 F7
            var buffer = new byte[] { 0xF0, 0x00, 0x20, 0x29, 0x02, 0x0D, 0x0E, 0x01, 0xF7 };
            MidiOutConnection?.SendBuffer(buffer);
            _initialized = true;
        }

        UpdateRangeLeds(Grid8x8,
                        mappedIndex =>
                        {
                            var color = LaunchpadColors.Off;
                            if (SymbolVariationPool.TryGetSnapshot(mappedIndex, out var v))
                            {
                                color = v.State switch
                                            {
                                                Variation.States.Undefined => LaunchpadColors.Off,
                                                Variation.States.InActive  => LaunchpadColors.Green,
                                                Variation.States.Active    => LaunchpadColors.Red,
                                                Variation.States.Modified  => LaunchpadColors.Yellow,
                                                _                          => color
                                            };
                            }

                            return AddModeHighlight(mappedIndex, (int)color);
                        });
    }

    private int AddModeHighlight(int index, int orgColor)
    {
        var indicatedStatus = (_updateCount + index / 8) % 30 < 4;
        if (!indicatedStatus) return orgColor;

        if (ActiveMode == InputModes.BlendTo) return (int)LaunchpadColors.LightBlue;
        if (ActiveMode == InputModes.Delete) return (int)LaunchpadColors.Red;

        return orgColor;
    }

    private int _updateCount;
    private bool _initialized;

    // --- Launchpad MK3 Grid Mapping (Programmer Mode) ---
    // The grid starts at 11 (bottom left is 11, top right is 88 in some modes, 
    // but in Programmer mode 81-88 is the top row).
    // We map 0-63 to the 8x8 grid.
    private static readonly ButtonRange Grid8x8 = new(11, 88); 
    
    // Side buttons (Scene Launch) - mapped to specific MIDI CC/Notes
    private static readonly ButtonRange SceneLaunchModeDelete = new(89); // Top right
    private static readonly ButtonRange SceneLaunchModeBlend = new(79); 
    private static readonly ButtonRange SceneLaunchStop = new(19); // Bottom right

    private enum LaunchpadColors
    {
        Off = 0,
        Red = 5,
        RedDim = 7,
        Yellow = 13,
        Orange = 9,
        Green = 21,
        Blue = 45,
        LightBlue = 37,
        White = 3,
        Pink = 53
    }
}