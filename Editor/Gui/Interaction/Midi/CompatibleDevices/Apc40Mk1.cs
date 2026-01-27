using System.Diagnostics.CodeAnalysis;
using NAudio;
using NAudio.Midi;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

/// <summary>
/// MIDI controller implementation for the Akai APC40 (original/Mk1).
/// 
/// The APC40 uses a simpler LED control scheme compared to Mk2 with only
/// 7 color states (off, green, green blinking, red, red blinking, orange, orange blinking).
/// 
/// The device is initialized to "Generic" mode (0x40) which allows basic LED control.
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
                      // Snapshot activation - press a clip button to activate/create snapshot
                      new(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      
                      // Snapshot save - hold Shift + press clip button to save
                      new(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      
                      // Snapshot delete - hold Scene Launch 1 + press clip button to delete
                      new(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      
                      // Stop blending - press Scene Launch 2 to stop blend operation
                      new(BlendActions.StopBlendingTowards, InputModes.Default, new[] { SceneLaunch2 },
                          CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                      
                      // Start blend towards - hold Scene Launch 2 + press clip button to start blend
                      new(BlendActions.StartBlendingTowardsSnapshot, requiredInputMode: InputModes.BlendTo, new[] { SceneTrigger1To40 },
                          CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),
                      
                      // Update blend progress with crossfader
                      new(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, new[] { AbFader },
                          CommandTriggerCombination.ExecutesAt.ControllerChange),
                      
                      // Update blend values with channel faders
                      new(BlendActions.UpdateBlendValues, InputModes.Default, new[] { Fader1To8 },
                          CommandTriggerCombination.ExecutesAt.ControllerChange),
                  };

        ModeButtons = new List<ModeButton>
                          {
                              new(SceneLaunch2, InputModes.BlendTo),
                              new(SceneLaunch1, InputModes.Delete),
                              new(Shift, InputModes.Save),
                          };
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;
        if (!_initialized)
        {
            // Initialize APC40 Mk1 in Ableton Live mode (0x41) for LED control
            // SysEx format: F0 47 00 73 60 00 04 <mode> <ver_major> <ver_minor> <ver_bugfix> F7
            Log.Debug("APC40 Mk1: Sending init SysEx message...");
            var buffer = new byte[]
                             {
                                 0xF0, // MIDI exclusive start
                                 0x47, // Manufacturers ID Byte (Akai)
                                 0x00, // System Exclusive Device ID
                                 0x73, // Product model ID (APC40)
                                 0x60, // Message type identifier (Introduction message)
                                 0x00, // Number of data bytes to follow (most significant)
                                 0x04, // Number of data bytes to follow (least significant) = 4 bytes
                                 0x41, // Application/Configuration Identifier (0x41=Ableton Live mode)
                                 0x08, // PC application Software version major
                                 0x01, // PC application Software version minor
                                 0x01, // PC application Software bug-fix level
                                 0xF7  // MIDI exclusive end
                             };
            MidiOutConnection?.SendBuffer(buffer);
            _initialized = true;
            Log.Debug("APC40 Mk1: Initialization complete");
        }

        // Log update cycle periodically
        if (_updateCount % 300 == 1)
        {
            //Log.Debug($"APC40 Mk1: Update cycle {_updateCount}, ActiveMode={ActiveMode}");
        }

        // Update clip launch button LEDs (5x8 grid)
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
                                                Variation.States.IsBlended => Apc40Mk1Colors.OrangeBlinking,
                                                _                          => color
                                            };
                            }

                            return AddModeHighlight(mappedIndex, (int)color);
                        });

        // Update scene launch button LEDs to show current mode
        UpdateSceneLaunchLeds();

    }

    /// <summary>
    /// Updates the scene launch button LEDs to indicate current input mode
    /// </summary>
    private void UpdateSceneLaunchLeds()
    {
        if (MidiOutConnection == null)
            return;

        // Scene Launch 1 (Delete mode indicator)
        var deleteModeColor = ActiveMode == InputModes.Delete 
            ? Apc40Mk1Colors.RedBlinking 
            : Apc40Mk1Colors.Red;
        SendColor(MidiOutConnection, 82, (int)deleteModeColor);

        // Scene Launch 2 (BlendTo mode indicator)
        var blendModeColor = ActiveMode == InputModes.BlendTo 
            ? Apc40Mk1Colors.OrangeBlinking 
            : Apc40Mk1Colors.Orange;
        SendColor(MidiOutConnection, 83, (int)blendModeColor);

        // Scene Launch 3-5 can show other states (currently off)
        SendColor(MidiOutConnection, 84, (int)Apc40Mk1Colors.Off);
        SendColor(MidiOutConnection, 85, (int)Apc40Mk1Colors.Off);
        SendColor(MidiOutConnection, 86, (int)Apc40Mk1Colors.Off);
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
                   InputModes.Save    => (int)Apc40Mk1Colors.GreenBlinking,
                   InputModes.BlendTo => (int)Apc40Mk1Colors.OrangeBlinking,
                   InputModes.Delete  => (int)Apc40Mk1Colors.RedBlinking,
                   _                  => orgColor
               };
    }

    /// <summary>
    /// Override SendColor to use the APC40 Mk1 specific channel mapping for LED control.
    /// 
    /// According to APC40 Communications Protocol:
    /// - Clip Launch grid (indices 0-39): Uses Notes 53-57 on Channels 1-8
    ///   index = ((note - 53) * 8) + (channel - 1), so:
    ///   note = (index / 8) + 53, channel = (index % 8) + 1
    /// - Other buttons: Channel 1 with note = button index
    /// </summary>
    protected override void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        if (CacheControllerColors[apcControlIndex] == colorCode)
            return;

        int channel;
        int noteNumber;
        
        // Clip launch grid buttons (0-39) need special channel/note mapping
        if (apcControlIndex >= 0 && apcControlIndex < 40)
        {
            // Reverse the mapping: index = ((note - 53) * 8) + (channel - 1)
            // So: note = (index / 8) + 53, channel = (index % 8) + 1
            int row = apcControlIndex / 8;       // 0-4
            int col = apcControlIndex % 8;       // 0-7
            noteNumber = row + 53;               // 53-57
            channel = col + 1;                   // 1-8
        }
        else
        {
            // Scene launch and other buttons use channel 1 with note = index
            channel = 1;
            noteNumber = apcControlIndex;
        }
        
        var noteOnEvent = new NoteOnEvent(0, channel, noteNumber, colorCode, 0);
        try
        {
            midiOut.Send(noteOnEvent.GetAsShortMessage());
        }
        catch (MmException e)
        {
            Log.Warning($"Failed setting midi color message for index {apcControlIndex}: {e.Message}");
        }

        CacheControllerColors[apcControlIndex] = colorCode;
    }

    /// <summary>
    /// Converts APC40 Mk1 MIDI channel/note to button index.
    /// 
    /// According to APC40 Communications Protocol:
    /// - Clip Launch grid: Notes 53-57 (rows 1-5) on Channels 1-8 (tracks/columns)
    ///   We convert to linear index 0-39: index = ((note - 53) * 8) + (channel - 1)
    /// - Other buttons use Channel 1 with their specific note numbers
    /// </summary>
    protected override int ConvertNoteToButtonId(int channel, int noteNumber)
    {
        // Clip launch grid: notes 53-57 on channels 1-8
        // This creates a 5 row x 8 column grid (40 buttons)
        if (noteNumber >= 53 && noteNumber <= 57 && channel >= 1 && channel <= 8)
        {
            // Convert to linear index 0-39
            // Note 53 on Ch1 = index 0, Note 53 on Ch2 = index 1, ..., Note 53 on Ch8 = index 7
            // Note 54 on Ch1 = index 8, Note 54 on Ch2 = index 9, ..., Note 54 on Ch8 = index 15
            // etc.
            int row = noteNumber - 53;  // 0-4
            int col = channel - 1;       // 0-7
            int index = (row * 8) + col;
            Log.Debug($"ConvertNoteToButtonId: Clip grid Note={noteNumber}, Channel={channel} -> row={row}, col={col}, ButtonId={index}");
            return index;
        }
        
        // All other buttons use note number directly
        return noteNumber;
    }

    private int _updateCount;

    // APC40 Mk1 Clip Launch Button Grid (8 columns x 5 rows = 40 buttons)
    // Notes 0-7 = Row 1, Notes 8-15 = Row 2, etc. (all on channel 1)
    // The grid layout from bottom-left to top-right:
    // Row 5: 32-39
    // Row 4: 24-31
    // Row 3: 16-23
    // Row 2: 8-15
    // Row 1: 0-7
    private static readonly ButtonRange SceneTrigger1To40 = new(0, 39);
    
    // Scene Launch buttons (right side of the grid)
    private static readonly ButtonRange SceneLaunch1To5 = new(82, 86);
    private static readonly ButtonRange SceneLaunch1 = new(82);
    private static readonly ButtonRange SceneLaunch2 = new(83);
    private static readonly ButtonRange SceneLaunch3 = new(84);
    private static readonly ButtonRange SceneLaunch4 = new(85);
    private static readonly ButtonRange SceneLaunch5 = new(86);
    
    // Track control buttons (below the clip grid)
    private static readonly ButtonRange ClipStopButtons1To8 = new(52, 59);
    private static readonly ButtonRange ClipSelectButtons1To8 = new(51, 51); // Note 51 with different channels
    private static readonly ButtonRange ClipSoloButtons1To8 = new(50, 50);   // Note 50 with different channels  
    private static readonly ButtonRange ClipRecArmButtons1To8 = new(48, 48); // Note 48 with different channels
    private static readonly ButtonRange ClipABButtons1To8 = new(66, 73);
    
    // Navigation buttons
    private static readonly ButtonRange BankSelectUp = new(94);
    private static readonly ButtonRange BankSelectDown = new(95);
    private static readonly ButtonRange BankSelectRight = new(96);
    private static readonly ButtonRange BankSelectLeft = new(97);
    private static readonly ButtonRange Shift = new(98);
    
    // Transport buttons
    private static readonly ButtonRange TapTempo = new(99);
    private static readonly ButtonRange NudgeMinus = new(100);
    private static readonly ButtonRange NudgePlus = new(101);
    private static readonly ButtonRange Session = new(102); // Also called "Clip/Track" on some models
    
    // Device control buttons
    private static readonly ButtonRange DeviceLeftArrow = new(58);
    private static readonly ButtonRange DeviceRightArrow = new(59);
    private static readonly ButtonRange BankLeftArrow = new(60);
    private static readonly ButtonRange BankRightArrow = new(61);
    private static readonly ButtonRange DevOnOff = new(62);
    private static readonly ButtonRange DevLock = new(63);
    private static readonly ButtonRange ClipDevView = new(64);
    private static readonly ButtonRange DetailView = new(65);
    
    // Mode buttons
    private static readonly ButtonRange Pan = new(87);
    private static readonly ButtonRange Sends = new(88);
    private static readonly ButtonRange User = new(89);
    private static readonly ButtonRange Metronome = new(90);
    private static readonly ButtonRange Play = new(91);
    private static readonly ButtonRange Stop = new(92);
    private static readonly ButtonRange Record = new(93);

    // Faders and knobs (Control Change messages)
    private static readonly ButtonRange Fader1To8 = new(7, 7);      // CC 7 on channels 1-8
    private static readonly ButtonRange MasterFader = new(14);       // CC 14 on channel 1
    private static readonly ButtonRange AbFader = new(15);           // CC 15 on channel 1 (Crossfader)
    private static readonly ButtonRange TopKnobs1To8 = new(48, 55); // CC 48-55 on channel 1
    private static readonly ButtonRange CueLevelKnob = new(47);      // CC 47 on channel 1
    private static readonly ButtonRange TempoKnob = new(13);         // CC 13 on channel 1
    private static readonly ButtonRange RightPerBankKnobs = new(16, 23); // CC 16-23 on channel 1

    /// <summary>
    /// APC40 Mk1 LED color values (sent as velocity in Note On messages)
    /// </summary>
    /// <remarks>
    /// The APC40 Mk1 uses a simple 7-state LED system for the clip launch grid.
    /// Reference: Akai APC40 Communications Protocol
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