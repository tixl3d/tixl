using NAudio;
using NAudio.Midi;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

// ReSharper disable InconsistentNaming, UnusedMember.Local, CommentTypo, StringLiteralTypo

/// <summary>
/// MIDI controller implementation for the Akai APC40 (original/Mk1).
/// 
/// The APC40 uses a simpler LED control scheme compared to Mk2 with only
/// 7 color states (off, green, green blinking, red, red blinking, orange, orange blinking).
/// 
/// The device is initialized to "Generic" mode (0x40) which allows basic LED control.
/// </summary>
[MidiDeviceProduct("Akai APC40")]
public sealed class Apc40Mk1 : CompatibleMidiDevice
{
    public Apc40Mk1()
    {
        CommandTriggerCombinations = 
            [
                    // Snapshot activate/create - press clip button to activate or create snapshot
                    new CommandTriggerCombination(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, [SceneTrigger1To40], 
                                                  CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                    // Snapshot save - hold Shift + press clip button to save
                    new CommandTriggerCombination(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, [SceneTrigger1To40], 
                                                  CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                    // Snapshot delete - hold Scene Launch 1 + press clip button to delete
                    new CommandTriggerCombination(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, [SceneTrigger1To40], 
                                                  CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                    // Blend between two snapshots - press two clip buttons simultaneously
                    new CommandTriggerCombination(BlendActions.StartBlendingSnapshots, InputModes.Default, [SceneTrigger1To40], 
                                                  CommandTriggerCombination.ExecutesAt.AllCombinedButtonsReleased),

                    // Start blend towards - hold Scene Launch 2 + press clip button to start blend
                    new CommandTriggerCombination(BlendActions.StartBlendingTowardsSnapshot, InputModes.BlendTo, [SceneTrigger1To40], 
                                                  CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                    // Stop blending - press Stop All Clips to stop blend operation
                    new CommandTriggerCombination(BlendActions.StopBlendingTowards, InputModes.Default, [ClipStopAll],
                                                 CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),

                    // Update blend progress with crossfader
                    new CommandTriggerCombination(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, [AbFader], 
                                                  CommandTriggerCombination.ExecutesAt.ControllerChange),

                    // Update blend progress with Master Fader
                    new CommandTriggerCombination(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, [MasterFader],
                                                  CommandTriggerCombination.ExecutesAt.ControllerChange),
                    
                    // Update blend values with channel faders
                    new CommandTriggerCombination(BlendActions.UpdateBlendValues, InputModes.Default, [Fader1To8],
                                                  CommandTriggerCombination.ExecutesAt.ControllerChange),

                    // Mode switching - Shift + Record/Arm 1/2/3 to switch between modes
                    // Record/Arm 1 = Generic passthrough (0x40), Record/Arm 2 = Ableton passthrough (0x41), Record/Arm 3 = Ableton control (0x41)
                    new CommandTriggerCombination(HandleModeSwitch, InputModes.Save, [RecordArmButtons],
                                                  CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed)

                ];

        ModeButtons =
            [
                new ModeButton(SceneLaunch1, InputModes.Delete),
                new ModeButton(SceneLaunch2, InputModes.BlendTo),
                new ModeButton(Shift, InputModes.Save)
            ];
    }

    /// <summary>
    /// Handles mode switching based on which Record/Arm button was pressed (with Shift held).
    /// Index 0 = Generic passthrough (0x40), Index 1 = Ableton passthrough (0x41), Index 2 = Ableton control (0x41)
    /// </summary>
    private void HandleModeSwitch(int index)
    {
        switch (index)
        {
            case 0: // Record/Arm 1 - Generic passthrough mode (0x40)
                LogMidiDebug("APC40 Mk1: Setting GENERIC PASSTHROUGH mode (0x40)");
                _useGenericMode = true;
                SendModeInitSysEx();
                SetControlMode(false);
                break;
                
            case 1: // Record/Arm 2 - Ableton passthrough mode (0x41)
                LogMidiDebug("APC40 Mk1: Setting ABLETON PASSTHROUGH mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(false);
                break;
                
            case 2: // Record/Arm 3 - Ableton control mode (0x41)
                LogMidiDebug("APC40 Mk1: Setting ABLETON CONTROL mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(true);
                break;
                
            default:
                LogMidiDebug($"APC40 Mk1: Ignoring mode switch for index {index}");
                return; // Don't clear signals for invalid index
        }
        
        // Clear button signals after mode switch to prevent stale signals from
        // blocking subsequent mode switches. The button mapping changes between
        // Generic and Ableton modes, so old signals may not match new button IDs.
        ClearButtonSignals();
    }

    /// <summary>
    /// Sends the SysEx initialization message to set the APC40 mode.
    /// Uses 0x40 (Generic) or 0x41 (Ableton Live) based on _useGenericMode flag.
    /// </summary>
    private void SendModeInitSysEx()
    {
        if (MidiOutConnection == null)
            return;
        
        // Clear all LEDs BEFORE mode switch
        // This clears using BOTH mode mappings to ensure all LEDs are off
        ClearAllLedsRaw();
            
        var modeIdentifier = _useGenericMode ? (byte)0x40 : (byte)0x41;
        LogMidiDebug($"APC40 Mk1: Sending mode SysEx (0x{modeIdentifier:X2})...");
        
        var buffer = new byte[]
                         {
                             0xF0, // MIDI exclusive start
                             0x47, // Manufacturers ID Byte (Akai)
                             0x00, // System Exclusive Device ID
                             0x73, // Product model ID (APC40)
                             0x60, // Message type identifier (Introduction message)
                             0x00, // Number of data bytes to follow (most significant)
                             0x04, // Number of data bytes to follow (least significant) = 4 bytes
                             modeIdentifier, // Application/Configuration Identifier (0x40=Generic, 0x41=Ableton Live mode)
                             0x08, // PC application Software version major
                             0x01, // PC application Software version minor
                             0x01, // PC application Software bug-fix level
                             0xF7  // MIDI exclusive end
                         };
        
        try
        {
            MidiOutConnection.SendBuffer(buffer);
            _initialized = true;
            LogMidiDebug($"APC40 Mk1: Mode switch complete (0x{modeIdentifier:X2})");
        }
        catch (Exception e)
        {
            Log.Warning($"APC40 Mk1: Failed to send mode SysEx: {e.Message}");
        }
        
        // Only update the mode indicator LED (Record/Arm 1, 2, or 3)
        // Don't update any other LEDs - let the normal update cycle handle that
        UpdateRecordArmModeLeds();
    }
    
    /// <summary>
    /// Clears all LEDs on the device by sending direct MIDI messages.
    /// Bypasses cache and clears using BOTH Generic and Ableton mode mappings.
    /// </summary>
    private void ClearAllLedsRaw()
    {
        if (MidiOutConnection == null)
            return;
        
        Array.Fill(CacheControllerColors, -1);
        
        // Clear clip grid - Generic mode (Notes 0-39 on Channel 1)
        foreach (var note in GenericClipGridNotes.Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);
        
        // Clear clip grid - Ableton mode (Notes 53-57 on Channels 1-8)
        foreach (var note in AbletonClipGridNotes.Indices())
            foreach (var ch in MidiChannels1To8.Indices())
                SendNoteRaw(ch, note, 0);
        
        // Clear scene launch LEDs
        foreach (var note in SceneLaunchNotes.Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);
        
        // Clear Record/Arm LEDs - Generic mode (Notes 48-55 on Channel 1)
        foreach (var note in GenericRecordArmNotes.Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);
        
        // Clear Record/Arm LEDs - Ableton mode (Note 48 on Channels 1-8)
        foreach (var ch in MidiChannels1To8.Indices())
            SendNoteRaw(ch, AbletonRecordArmNote, 0);
    }
    
    /// <summary>
    /// Sends a raw MIDI note, bypassing cache. Used for LED clearing.
    /// </summary>
    private void SendNoteRaw(int channel, int note, int velocity)
    {
        try
        {
            var evt = new NoteOnEvent(0, channel, note, velocity, 0);
            MidiOutConnection?.Send(evt.GetAsShortMessage());
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to send MIDI note (ch={channel}, note={note}): {e.Message}");
        }
    }

    /// <summary>
    /// Called when control mode changes. Reinitialize device when entering control mode.
    /// </summary>
    protected override void OnControlModeChanged(bool isNowInControlMode)
    {
        base.OnControlModeChanged(isNowInControlMode);
        
        if (isNowInControlMode)
        {
            // Re-entering control mode - reinitialize the device
            _initialized = false;
        }
        
        // Update Record/Arm LEDs to show current mode
        UpdateRecordArmModeLeds();
    }

    /// <summary>
    /// Updates Record/Arm 1, 2, and 3 LEDs to show current mode.
    /// Green = active mode, Off = inactive
    /// </summary>
    private void UpdateRecordArmModeLeds()
    {
        if (MidiOutConnection == null)
            return;

        var modeLedColors = new[]
        {
            _useGenericMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off,                        // Generic passthrough
            !_useGenericMode && !IsInControlMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off,   // Ableton passthrough
            !_useGenericMode && IsInControlMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off     // Ableton control
        };
        
        for (var i = 0; i < modeLedColors.Length; i++)
        {
            var channel = _useGenericMode ? MidiChannels1To8.StartIndex : MidiChannels1To8.StartIndex + i;
            var note = _useGenericMode ? GenericRecordArmNotes.StartIndex + i : AbletonRecordArmNote;
            SendNoteRaw(channel, note, (int)modeLedColors[i]);
        }
    }

    /// <summary>
    /// Clears all LEDs when in passthrough mode. Shows mode highlighting when Shift is held.
    /// </summary>
    protected override void ClearDeviceLeds()
    {
        if (MidiOutConnection == null)
            return;

        _updateCount++;
        
        // Clear clip launch grid LEDs or show mode highlight
        for (var i = 0; i < ClipGridSize; i++)
        {
            if (ActiveMode != InputModes.Default)
                CacheControllerColors[i] = -1;
            
            SendColor(MidiOutConnection, i, AddModeHighlight(i, (int)Apc40Mk1Colors.Off));
        }
        
        // Clear scene launch LEDs
        foreach (var i in SceneLaunchNotes.Indices())
        {
            CacheControllerColors[i] = -1;
            SendColor(MidiOutConnection, i, (int)Apc40Mk1Colors.Off);
        }
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;
        if (!_initialized)
            SendModeInitSysEx();

        // Update clip launch button LEDs (5x8 grid)
        UpdateRangeLeds(SceneTrigger1To40, mappedIndex =>
        {
            if (!SymbolVariationPool.TryGetSnapshot(mappedIndex, out var v))
                return AddModeHighlight(mappedIndex, (int)Apc40Mk1Colors.Off);
            
            var isBlendTarget = BlendActions.BlendTowardsIndex == mappedIndex;
            var color = v.State switch
            {
                Variation.States.Active    => Apc40Mk1Colors.Red,
                Variation.States.Modified  => Apc40Mk1Colors.Orange,
                Variation.States.IsBlended => Apc40Mk1Colors.OrangeBlinking,
                Variation.States.InActive  => isBlendTarget ? Apc40Mk1Colors.OrangeBlinking : Apc40Mk1Colors.Green,
                _                          => Apc40Mk1Colors.Off
            };

            return AddModeHighlight(mappedIndex, (int)color);
        });

        // Update scene launch LEDs only in Ableton control mode
        if (IsInControlMode && !_useGenericMode)
            UpdateSceneLaunchLeds();
    }

    /// <summary>
    /// Updates the scene launch button LEDs to indicate current input mode.
    /// </summary>
    private void UpdateSceneLaunchLeds()
    {
        if (MidiOutConnection == null)
            return;

        var colors = new[]
        {
            ActiveMode == InputModes.Delete ? Apc40Mk1Colors.RedBlinking : Apc40Mk1Colors.Red,
            ActiveMode == InputModes.BlendTo ? Apc40Mk1Colors.OrangeBlinking : Apc40Mk1Colors.Orange,
            Apc40Mk1Colors.Off,
            Apc40Mk1Colors.Off,
            Apc40Mk1Colors.Off
        };

        for (var i = 0; i < colors.Length; i++)
            SendColor(MidiOutConnection, SceneLaunchNotes.StartIndex + i, (int)colors[i]);
    }

    private int AddModeHighlight(int index, int orgColor)
    {
        // Software-based flashing using solid colors
        var indicatedStatus = (_updateCount + index / AbletonClipGridColumns) % 30 < 4;
        if (!indicatedStatus)
        {
            return orgColor;
        }

        return ActiveMode switch
               {
                   InputModes.Save    => (int)Apc40Mk1Colors.Green,
                   InputModes.BlendTo => (int)Apc40Mk1Colors.Orange,
                   InputModes.Delete  => (int)Apc40Mk1Colors.Red,
                   _                  => orgColor
               };
    }

    /// <summary>
    /// Sends LED color using APC40 Mk1 specific channel mapping.
    /// Generic mode: Notes 0-39 on Channel 1. Ableton mode: Notes 53-57 on Channels 1-8.
    /// </summary>
    protected override void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        if (apcControlIndex < 0 || apcControlIndex >= CacheControllerColors.Length)
            return;

        if (CacheControllerColors[apcControlIndex] == colorCode)
            return;

        int channel, noteNumber;

        if (apcControlIndex < ClipGridSize)
        {
            if (_useGenericMode)
            {
                channel = MidiChannels1To8.StartIndex;
                noteNumber = apcControlIndex;
            }
            else
            {
                var row = apcControlIndex / AbletonClipGridColumns;
                var col = apcControlIndex % AbletonClipGridColumns;
                noteNumber = row + AbletonClipGridNotes.StartIndex;
                channel = col + MidiChannels1To8.StartIndex;
            }
        }
        else
        {
            channel = MidiChannels1To8.StartIndex;
            noteNumber = apcControlIndex;
        }

        try
        {
            var noteOnEvent = new NoteOnEvent(0, channel, noteNumber, colorCode, 0);
            midiOut.Send(noteOnEvent.GetAsShortMessage());
            CacheControllerColors[apcControlIndex] = colorCode;
        }
        catch (MmException e)
        {
            Log.Warning($"Failed setting midi color for index {apcControlIndex}: {e.Message}");
        }
    }

    /// <summary>
    /// Converts APC40 Mk1 MIDI channel/note to button index.
    /// Mapping differs between Generic Mode (0x40) and Ableton Live Mode (0x41).
    /// </summary>
    protected override int ConvertNoteToButtonId(int channel, int noteNumber)
    {
        // Shift button - same in both modes
        if (noteNumber == ShiftButtonNote && channel == MidiChannels1To8.StartIndex)
        {
            LogMidiDebug($"ConvertNoteToButtonId: Shift button Note={noteNumber}, Channel={channel} -> ButtonId={ShiftButtonNote}");
            return ShiftButtonNote;
        }
        
        // Clip launch grid
        if (_useGenericMode)
        {
            // Generic: Notes 0-39 on Channel 1
            if (GenericClipGridNotes.IncludesButtonIndex(noteNumber) && channel == MidiChannels1To8.StartIndex)
            {
                LogMidiDebug($"ConvertNoteToButtonId [Generic]: Clip grid Note={noteNumber}, Channel={channel} -> ButtonId={noteNumber}");
                return noteNumber;
            }
        }
        else
        {
            // Ableton: Notes 53-57 on Channels 1-8 (5x8 grid)
            if (AbletonClipGridNotes.IncludesButtonIndex(noteNumber) && MidiChannels1To8.IncludesButtonIndex(channel))
            {
                var row = AbletonClipGridNotes.GetMappedIndex(noteNumber);
                var col = MidiChannels1To8.GetMappedIndex(channel);
                var buttonId = row * AbletonClipGridColumns + col;
                LogMidiDebug($"ConvertNoteToButtonId [Ableton]: Clip grid Note={noteNumber}, Channel={channel} -> row={row}, col={col}, ButtonId={buttonId}");
                return buttonId;
            }
        }
        
        // Record/Arm buttons - must work in both modes for switching
        if (noteNumber == AbletonRecordArmNote && MidiChannels1To8.IncludesButtonIndex(channel))
        {
            var buttonId = RecordArmBaseId + MidiChannels1To8.GetMappedIndex(channel);
            LogMidiDebug($"ConvertNoteToButtonId: Record/Arm (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
            return buttonId;
        }

        switch (_useGenericMode)
        {
            case true when channel == MidiChannels1To8.StartIndex:
            {
                // Generic Record/Arm: Notes 49-55 on Channel 1 (48 handled above)
                if (GenericRecordArmNotes.IncludesButtonIndex(noteNumber) && noteNumber != AbletonRecordArmNote)
                {
                    var buttonId = RecordArmBaseId + GenericRecordArmNotes.GetMappedIndex(noteNumber);
                    LogMidiDebug($"ConvertNoteToButtonId: Record/Arm (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                    return buttonId;
                }
            
                // Generic Track Select: Notes 58-65 on Channel 1
                if (GenericTrackSelectNotes.IncludesButtonIndex(noteNumber))
                {
                    var buttonId = TrackSelectBaseId + GenericTrackSelectNotes.GetMappedIndex(noteNumber);
                    LogMidiDebug($"ConvertNoteToButtonId: Track Select (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                    return buttonId;
                }

                break;
            }
            // Ableton Track Select: Note 51 on Channels 1-8
            case false when noteNumber == AbletonTrackSelectNote && MidiChannels1To8.IncludesButtonIndex(channel):
            {
                var buttonId = TrackSelectBaseId + MidiChannels1To8.GetMappedIndex(channel);
                LogMidiDebug($"ConvertNoteToButtonId: Track Select (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                return buttonId;
            }
        }

        // Default fallback - use note number directly
        return noteNumber;
    }
    
    // Base ID for track select buttons to avoid collision with other button IDs
    private const int TrackSelectBaseId = 1000;

    #region MIDI Note/Channel Mapping Constants
    
    // ===== Common to both modes =====
    private const int ShiftButtonNote = 98;
    private const int ClipGridSize = 40;  // 5 rows x 8 columns
    
    // ===== Channel range for multi-channel mappings =====
    private static readonly ButtonRange MidiChannels1To8 = new(1, 8);
    
    // ===== Generic Mode (0x40) MIDI Mappings =====
    // Clip Launch Grid: Notes 0-39 on Channel 1
    private static readonly ButtonRange GenericClipGridNotes = new(0, 39);
    
    // Record/Arm: Notes 48-55 on Channel 1
    private static readonly ButtonRange GenericRecordArmNotes = new(48, 55);
    
    // Track Select: Notes 58-65 on Channel 1
    private static readonly ButtonRange GenericTrackSelectNotes = new(58, 65);
    
    // ===== Ableton Live Mode (0x41) MIDI Mappings =====
    // Clip Launch Grid: Notes 53-57 on Channels 1-8 (5 rows x 8 columns)
    private static readonly ButtonRange AbletonClipGridNotes = new(53, 57);
    private const int AbletonClipGridColumns = 8;
    
    // Record/Arm: Note 48 on Channels 1-8
    private const int AbletonRecordArmNote = 48;
    
    // Track Select: Note 51 on Channels 1-8
    private const int AbletonTrackSelectNote = 51;
    
    // ===== Scene Launch buttons (same in both modes) =====
    private static readonly ButtonRange SceneLaunchNotes = new(82, 86);
    
    #endregion

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
    
    // Record/Arm buttons (bottom row - used for mode switching with Shift)
    // In Ableton mode: Note 48 on Channels 1-8
    // In Generic mode: Notes 50-57 on Channel 1
    // Mapped to button IDs 2000-2007 via ConvertNoteToButtonId
    private const int RecordArmBaseId = 2000;
    private static readonly ButtonRange RecordArmButtons = new(RecordArmBaseId, RecordArmBaseId + 7);
    
    // Track Select buttons (mapped to button IDs 1000-1007 via ConvertNoteToButtonId)
    private static readonly ButtonRange TrackSelectButtons = new(TrackSelectBaseId, TrackSelectBaseId + 7);
    
    // Stop all clips button
    private static readonly ButtonRange ClipStopAll = new(81);
    
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
        OrangeBlinking = 6
    }

    /// <summary>
    /// Logs a debug message if MIDI debug logging is enabled in settings.
    /// </summary>
    private static void LogMidiDebug(string message)
    {
        if (UserSettings.Config.EnableMidiDebugLogging)
            Log.Debug(message);
    }

    private bool _initialized;
    private bool _useGenericMode = true; // Default to Generic passthrough mode (0x40)
}
