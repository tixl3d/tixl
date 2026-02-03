using NAudio;
using NAudio.Midi;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

// ReSharper disable InconsistentNaming, UnusedMember.Local, CommentTypo, StringLiteralTypo

/// <summary>
/// MIDI controller implementation for the Akai APC40 Mk2.
/// 
/// The APC40 Mk2 uses RGB LED colors (128 color palette) compared to Mk1's 7 color states.
/// 
/// The device can be initialized to "Generic" mode (0x40), "Ableton" mode (0x41), 
/// or "Ableton with full control" mode (0x42).
/// 
/// Reference: APC40Mk2_Communications_Protocol_v1.2.pdf
/// </summary>

[MidiDeviceProduct("APC40 mkII")]
public sealed class Apc40Mk2 : CompatibleMidiDevice
{
    public Apc40Mk2()
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

                // Stop blending - press Scene Launch 2 to stop blend operation
                new CommandTriggerCombination(BlendActions.StopBlendingTowards, InputModes.Default, [SceneLaunch2],
                                              CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),
                
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
    /// Index 0 = Generic passthrough (0x40), Index 1 = Ableton passthrough (0x41), Index 2 = Ableton control (0x42)
    /// </summary>
    private void HandleModeSwitch(int index)
    {
        switch (index)
        {
            case 0: // Record/Arm 1 - Generic passthrough mode (0x40)
                LogMidiDebug("APC40 Mk2: Setting GENERIC PASSTHROUGH mode (0x40)");
                _useGenericMode = true;
                SendModeInitSysEx();
                SetControlMode(false);
                break;
                
            case 1: // Record/Arm 2 - Ableton passthrough mode (0x41)
                LogMidiDebug("APC40 Mk2: Setting ABLETON PASSTHROUGH mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(false);
                break;
                
            case 2: // Record/Arm 3 - Ableton control mode (0x41)
                LogMidiDebug("APC40 Mk2: Setting ABLETON CONTROL mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(true);
                break;
                
            default:
                LogMidiDebug($"APC40 Mk2: Ignoring mode switch for index {index}");
                return; // Don't clear signals for invalid index
        }
        
        // Clear button signals after mode switch to prevent stale signals from
        // blocking subsequent mode switches. The button mapping changes between
        // Generic and Ableton modes, so old signals may not match new button IDs.
        ClearButtonSignals();
    }

    /// <summary>
    /// Sends the SysEx initialization message to set the APC40 Mk2 mode.
    /// Uses 0x40 (Generic), 0x41 (Ableton), or 0x42 (Ableton with full control).
    /// </summary>
    private void SendModeInitSysEx()
    {
        if (MidiOutConnection == null)
            return;
        
        // Clear all LEDs BEFORE mode switch
        // This clears using BOTH mode mappings to ensure all LEDs are off
        ClearAllLedsRaw();
            
        var modeIdentifier = _useGenericMode ? (byte)0x40 : (byte)0x41;
        LogMidiDebug($"APC40 Mk2: Sending mode SysEx (0x{modeIdentifier:X2})...");
        
        var buffer = new byte[]
                         {
                             0xF0, // MIDI exclusive start
                             0x47, // Manufacturers ID Byte (Akai)
                             0x7F, // System Exclusive Device ID (0x7F = any)
                             0x29, // Product model ID (APC40 Mk2)
                             0x60, // Message type identifier (Introduction message)
                             0x00, // Number of data bytes to follow (most significant)
                             0x04, // Number of data bytes to follow (least significant) = 4 bytes
                             modeIdentifier, // Application/Configuration Identifier
                             0x01, // PC application Software version major
                             0x01, // PC application Software version minor
                             0x01, // PC application Software bug-fix level
                             0xF7  // MIDI exclusive end
                         };

        try
        {
            MidiOutConnection.SendBuffer(buffer);
            _initialized = true;
            LogMidiDebug($"APC40 Mk2: Mode switch complete (0x{modeIdentifier:X2})");
        }
        catch (Exception e)
        {
            Log.Warning($"APC40 Mk2: Failed to send mode SysEx: {e.Message}");
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

        // Clear clip grid - Ableton mode (Notes 0-4 on Channels 1-8)
        foreach (var note in AbletonClipGridNotes.Indices())
            foreach (var ch in MidiChannels1To8.Indices())
                SendNoteRaw(ch, note, 0);

        // Clear scene launch LEDs
        foreach (var note in SceneLaunchNotes.Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);

        // Clear Record/Arm LEDs - Ableton mode (Note 0 on Channels 1-8)
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
            _initialized = false;
        }

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
            _useGenericMode ? Apc40Mk2Colors.Green : Apc40Mk2Colors.Off,                        // Generic passthrough
            !_useGenericMode && !IsInControlMode ? Apc40Mk2Colors.Green : Apc40Mk2Colors.Off,   // Ableton passthrough
            !_useGenericMode && IsInControlMode ? Apc40Mk2Colors.Green : Apc40Mk2Colors.Off     // Ableton control
        };

        for (var i = 0; i < modeLedColors.Length; i++)
        {
            var channel = _useGenericMode ? MidiChannels1To8.StartIndex : MidiChannels1To8.StartIndex + i;
            var note = _useGenericMode ? i : AbletonRecordArmNote;
            SendNoteRaw(channel, note, (int)modeLedColors[i]);
        }
    }

    /// <summary>
    /// Clears all LEDs when in passthrough mode. Shows mode highlighting when a modifier is held.
    /// </summary>
    protected override void ClearDeviceLeds()
    {
        if (MidiOutConnection == null)
            return;

        _updateCount++;

        for (var i = 0; i < ClipGridSize; i++)
        {
            if (ActiveMode != InputModes.Default)
                CacheControllerColors[i] = -1;

            SendLedState(MidiOutConnection, i, AddModeHighlight(i, LedState.Off));
        }

        foreach (var i in SceneLaunchNotes.Indices())
        {
            CacheControllerColors[i] = -1;
            SendLedState(MidiOutConnection, i, LedState.Off);
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
                return AddModeHighlight(mappedIndex, LedState.Off).ToCacheKey();
            
            var isBlendTarget = BlendActions.BlendTowardsIndex == mappedIndex;
            var state = v.State switch
            {
                Variation.States.Active    => new LedState(Apc40Mk2Colors.Red),
                Variation.States.Modified  => new LedState(Apc40Mk2Colors.Yellow),
                Variation.States.IsBlended => new LedState(Apc40Mk2Colors.Orange, LedBehavior.Pulse1_16),
                Variation.States.InActive  => isBlendTarget 
                                                ? new LedState(Apc40Mk2Colors.Orange, LedBehavior.Pulse1_16)
                                                : new LedState(Apc40Mk2Colors.Green),
                _                          => LedState.Off
            };

            return AddModeHighlight(mappedIndex, state).ToCacheKey();
        });

        // Update scene launch LEDs only in Ableton control mode
        if (IsInControlMode && !_useGenericMode)
            UpdateSceneLaunchLeds();
    }

    /// <summary>
    /// Represents an LED state with color and behavior.
    /// </summary>
    private readonly record struct LedState(Apc40Mk2Colors Color, LedBehavior Behavior = LedBehavior.Solid)
    {
        public static readonly LedState Off = new(Apc40Mk2Colors.Off);
        
        /// <summary>
        /// Converts to a cache key for change detection.
        /// </summary>
        public int ToCacheKey() => ((int)Behavior << 8) | (int)Color;
    }

    /// <summary>
    /// Updates the scene launch button LEDs to indicate current input mode.
    /// </summary>
    private void UpdateSceneLaunchLeds()
    {
        if (MidiOutConnection == null)
            return;

        var states = new[]
        {
            ActiveMode == InputModes.Delete ? new LedState(Apc40Mk2Colors.Red, LedBehavior.Blink1_4) : new LedState(Apc40Mk2Colors.Red),
            ActiveMode == InputModes.BlendTo ? new LedState(Apc40Mk2Colors.Orange, LedBehavior.Pulse1_16) : new LedState(Apc40Mk2Colors.Orange),
            LedState.Off,
            LedState.Off,
            LedState.Off
        };

        for (var i = 0; i < states.Length; i++)
            SendLedState(MidiOutConnection, SceneLaunchNotes.StartIndex + i, states[i]);
    }

    private LedState AddModeHighlight(int index, LedState state)
    {
        var indicatedStatus = (_updateCount + index / AbletonClipGridColumns) % 30 < 4;
        if (!indicatedStatus)
            return state;

        return ActiveMode switch
        {
            InputModes.Save    => new LedState(Apc40Mk2Colors.Green, LedBehavior.Pulse1_8),
            InputModes.BlendTo => new LedState(Apc40Mk2Colors.Orange, LedBehavior.Pulse1_8),
            InputModes.Delete  => new LedState(Apc40Mk2Colors.Red, LedBehavior.Pulse1_8),
            _                  => state
        };
    }

    /// <summary>
    /// APC40 Mk2 LED behavior types.
    /// For non-grid buttons, behavior is controlled via MIDI channel 1-8.
    /// For clip grid in Ableton mode, behavior uses channels 9-15:
    /// - Channel 1-8: Solid (track columns 1-8)
    /// - Channel 9: Pulsing 1/16
    /// - Channel 10: Pulsing 1/8
    /// - Channel 11: Blinking 1/24
    /// - Channel 12: Blinking 1/16
    /// - Channel 13: Blinking 1/8
    /// - Channel 14: Blinking 1/4
    /// - Channel 15: Blinking 1/2
    /// </summary>
    private enum LedBehavior
    {
        Solid = 0,
        Pulse1_16 = 1,
        Pulse1_8 = 2,
        Blink1_24 = 3,
        Blink1_16 = 4,
        Blink1_8 = 5,
        Blink1_4 = 6,
        Blink1_2 = 7,
    }

    /// <summary>
    /// Sends an LED state to a button on the APC40 Mk2.
    /// 
    /// Generic Mode: Clip grid uses Notes 0-39 on Channel 1 (solid only).
    /// Ableton Mode: Clip grid uses Notes 0-4, with channels controlling behavior:
    /// - Channel 1-8: Solid for track columns 1-8
    /// - Channel 9-15: Behaviors (Pulse/Blink) using note to encode row+column
    /// </summary>
    private void SendLedState(MidiOut midiOut, int controlIndex, LedState state)
    {
        if (midiOut == null || controlIndex < 0 || controlIndex >= CacheControllerColors.Length)
            return;

        var cacheKey = state.ToCacheKey();
        if (CacheControllerColors[controlIndex] == cacheKey)
            return;

        int channel;
        int noteNumber;

        if (controlIndex < ClipGridSize)
        {
            if (_useGenericMode)
            {
                // Generic mode: Notes 0-39 on Channel 1, solid only
                channel = MidiChannels1To8.StartIndex;
                noteNumber = controlIndex;
            }
            else
            {
                // Ableton mode
                int row = controlIndex / AbletonClipGridColumns;
                int col = controlIndex % AbletonClipGridColumns;
                
                if (state.Behavior == LedBehavior.Solid)
                {
                    // Solid: Channel = track column (1-8), Note = row (0-4)
                    channel = col + MidiChannels1To8.StartIndex;
                    noteNumber = row + AbletonClipGridNotes.StartIndex;
                }
                else
                {
                    // Behaviors: Channel 9-15, Note = grid index (0-39)
                    channel = 8 + (int)state.Behavior;
                    noteNumber = controlIndex;
                }
            }
        }
        else
        {
            // Non-grid buttons: channel 1-8 for behaviors
            channel = MidiChannels1To8.StartIndex + (int)state.Behavior;
            noteNumber = controlIndex;
        }

        var noteOnEvent = new NoteOnEvent(0, channel, noteNumber, (int)state.Color, 0);
        try
        {
            midiOut.Send(noteOnEvent.GetAsShortMessage());
        }
        catch (MmException e)
        {
            Log.Warning($"Failed setting LED state for index {controlIndex}: {e.Message}");
        }

        CacheControllerColors[controlIndex] = cacheKey;
    }

    /// <summary>
    /// Override required by base class - delegates to SendLedState.
    /// </summary>
    protected override void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        // Decode legacy color code format
        var color = (Apc40Mk2Colors)(colorCode & 0xFF);
        var behavior = (LedBehavior)((colorCode >> 8) & 0xFF);
        SendLedState(midiOut, apcControlIndex, new LedState(color, behavior));
    }

    /// <summary>
    /// Converts APC40 Mk2 MIDI channel/note to button index.
    /// Generic Mode: Clip grid = Notes 0-39 on Ch1, Track Select = Notes 58-65 on Ch1.
    /// Ableton Mode: Clip grid = Notes 0-4 on Ch1-8, Track Select = Note 51 on Ch1-8.
    /// </summary>
    protected override int ConvertNoteToButtonId(int channel, int noteNumber)
    {
        // Shift button - same in both modes
        if (noteNumber == ShiftButtonNote && channel == MidiChannels1To8.StartIndex)
        {
            LogMidiDebug($"ConvertNoteToButtonId: Shift button Note={noteNumber}, Channel={channel} -> ButtonId={ShiftButtonNote}");
            return ShiftButtonNote;
        }

        // Clip Launch Grid
        if (_useGenericMode)
        {
            if (GenericClipGridNotes.IncludesButtonIndex(noteNumber) && channel == MidiChannels1To8.StartIndex)
            {
                LogMidiDebug($"ConvertNoteToButtonId [Generic]: Clip grid Note={noteNumber}, Channel={channel} -> ButtonId={noteNumber}");
                return noteNumber;
            }
        }
        else
        {
            if (AbletonClipGridNotes.IncludesButtonIndex(noteNumber) && MidiChannels1To8.IncludesButtonIndex(channel))
            {
                var row = AbletonClipGridNotes.GetMappedIndex(noteNumber);
                var col = MidiChannels1To8.GetMappedIndex(channel);
                var buttonId = row * AbletonClipGridColumns + col;
                LogMidiDebug($"ConvertNoteToButtonId [Ableton]: Clip grid Note={noteNumber}, Channel={channel} -> row={row}, col={col}, ButtonId={buttonId}");
                return buttonId;
            }
        }

        // Record/Arm buttons for mode switching
        if (noteNumber == AbletonRecordArmNote && MidiChannels1To8.IncludesButtonIndex(channel))
        {
            var buttonId = RecordArmBaseId + MidiChannels1To8.GetMappedIndex(channel);
            LogMidiDebug($"ConvertNoteToButtonId: Record/Arm (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
            return buttonId;
        }

        // Generic mode additional Record/Arm notes
        if (_useGenericMode && channel == MidiChannels1To8.StartIndex
            && GenericRecordArmNotes.IncludesButtonIndex(noteNumber))
        {
            var buttonId = RecordArmBaseId + GenericRecordArmNotes.GetMappedIndex(noteNumber);
            LogMidiDebug($"ConvertNoteToButtonId: Record/Arm (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
            return buttonId;
        }

        // Track Select buttons
        if (_useGenericMode)
        {
            if (channel == MidiChannels1To8.StartIndex && GenericTrackSelectNotes.IncludesButtonIndex(noteNumber))
            {
                var buttonId = TrackSelectBaseId + GenericTrackSelectNotes.GetMappedIndex(noteNumber);
                LogMidiDebug($"ConvertNoteToButtonId: Track Select (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                return buttonId;
            }
        }
        else
        {
            if (noteNumber == AbletonTrackSelectNote && MidiChannels1To8.IncludesButtonIndex(channel))
            {
                var buttonId = TrackSelectBaseId + MidiChannels1To8.GetMappedIndex(channel);
                LogMidiDebug($"ConvertNoteToButtonId: Track Select (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                return buttonId;
            }
        }

        return noteNumber;
    }

    private const int TrackSelectBaseId = 1000;

    #region MIDI Note/Channel Mapping Constants

    private const int ShiftButtonNote = 98;
    private const int ClipGridSize = 40;

    private static readonly ButtonRange MidiChannels1To8 = new(1, 8);

    // Generic Mode (0x40) MIDI Mappings
    private static readonly ButtonRange GenericClipGridNotes = new(0, 39);
    private static readonly ButtonRange GenericRecordArmNotes = new(48, 55);
    private static readonly ButtonRange GenericTrackSelectNotes = new(58, 65);

    // Ableton Live Mode (0x41/0x42) MIDI Mappings
    // Clip Launch Grid: Notes 0-4 on Channels 1-8 (5 rows x 8 columns)
    private static readonly ButtonRange AbletonClipGridNotes = new(0, 4);
    private const int AbletonClipGridColumns = 8;
    private const int AbletonRecordArmNote = 48;
    private const int AbletonTrackSelectNote = 51;

    private static readonly ButtonRange SceneLaunchNotes = new(82, 86);

    #endregion

    private int _updateCount;

    // Clip Launch Button Grid
    private static readonly ButtonRange SceneTrigger1To40 = new(0, 39);
    
    // Scene Launch buttons
    private static readonly ButtonRange SceneLaunch1To5 = new(82, 86);
    private static readonly ButtonRange SceneLaunch1 = new(82);
    private static readonly ButtonRange SceneLaunch2 = new(83);
    private static readonly ButtonRange SceneLaunch3 = new(84);
    private static readonly ButtonRange SceneLaunch4 = new(85);
    private static readonly ButtonRange SceneLaunch5 = new(86);

    // Track control buttons
    private static readonly ButtonRange ClipStopButtons1To8 = new(52, 59);
    private static readonly ButtonRange ClipABButtons1To8 = new(66, 73);

    // Record/Arm buttons (mapped to button IDs 2000-2007)
    private const int RecordArmBaseId = 2000;
    private static readonly ButtonRange RecordArmButtons = new(RecordArmBaseId, RecordArmBaseId + 7);

    // Stop all clips button
    private static readonly ButtonRange ClipStopAll = new(81);
    
    // Navigation buttons
    private static readonly ButtonRange BankSelectTop = new(94);
    private static readonly ButtonRange BankSelectRight = new(96);
    private static readonly ButtonRange BankSelectBottom = new(95);
    private static readonly ButtonRange BankSelectLeft = new(97);
    private static readonly ButtonRange Shift = new(98);
    private static readonly ButtonRange Bank = new(103);

    // Device control buttons
    private static readonly ButtonRange DetailView = new(65);
    private static readonly ButtonRange ClipDevView = new(64);
    private static readonly ButtonRange DevLock = new(63);
    private static readonly ButtonRange DevOnOff = new(62);
    private static readonly ButtonRange BankRightArrow = new(61);
    private static readonly ButtonRange BankLeftArrow = new(60);
    private static readonly ButtonRange DeviceRightArrow = new(59);
    private static readonly ButtonRange DeviceLeftArrow = new(58);

    // Transport and tempo buttons
    private static readonly ButtonRange NudgePlus = new(101);
    private static readonly ButtonRange NudgeMinus = new(100);
    private static readonly ButtonRange User = new(89);
    private static readonly ButtonRange TapTempo = new(99);
    private static readonly ButtonRange Metronome = new(90);
    private static readonly ButtonRange Sends = new(88);
    private static readonly ButtonRange Pan = new(87);
    private static readonly ButtonRange Play = new(91);
    private static readonly ButtonRange Stop = new(92);
    private static readonly ButtonRange Record = new(93);
    private static readonly ButtonRange Session = new(102);

    // Faders and knobs (Control Change messages)
    private static readonly ButtonRange Fader1To8 = new(7, 7);
    private static readonly ButtonRange RightPerBankKnobs = new(16, 23);
    private static readonly ButtonRange MasterFader = new(14);
    private static readonly ButtonRange AbFader = new(15);
    private static readonly ButtonRange TopKnobs1To8 = new(48, 55);
    private static readonly ButtonRange CueLevelKnob = new(47);
    private static readonly ButtonRange TempoKnob = new(13);

    /// <summary>
    /// APC40 Mk2 RGB LED color values (128 color palette).
    /// Reference: APC40Mk2_Communications_Protocol_v1.2.pdf
    /// Each row of 4 represents the same hue at different brightness levels:
    /// [Bright/Light] [Normal] [Dark] [Dim]
    /// </summary>
    private enum Apc40Mk2Colors
    {
        // Row 0: Grayscale
        Off = 0,                    // #000000
        DarkGray = 1,               // #1E1E1E
        Gray = 2,                   // #7F7F7F
        White = 3,                  // #FFFFFF

        // Row 1: Red
        LightRed = 4,               // #FF4C4C
        Red = 5,                    // #FF0000
        DarkRed = 6,                // #590000
        DimRed = 7,                 // #190000

        // Row 2: Orange
        LightOrange = 8,            // #FFBD6C
        Orange = 9,                 // #FF5400
        DarkOrange = 10,            // #591D00
        DimOrange = 11,             // #271B00

        // Row 3: Yellow
        LightYellow = 12,           // #FFFF4C
        Yellow = 13,                // #FFFF00
        DarkYellow = 14,            // #595900
        DimYellow = 15,             // #191900

        // Row 4: Chartreuse (Yellow-Green)
        LightChartreuse = 16,       // #88FF4C
        Chartreuse = 17,            // #54FF00
        DarkChartreuse = 18,        // #1D5900
        DimChartreuse = 19,         // #142B00

        // Row 5: Green
        LightGreen = 20,            // #4CFF4C
        Green = 21,                 // #00FF00
        DarkGreen = 22,             // #005900
        DimGreen = 23,              // #001900

        // Row 6: Spring Green (Green with hint of Cyan)
        LightSpringGreen = 24,      // #4CFF5E
        SpringGreen = 25,           // #00FF19
        DarkSpringGreen = 26,       // #00590D
        DimSpringGreen = 27,        // #001902

        // Row 7: Turquoise Green
        LightTurquoiseGreen = 28,   // #4CFF88
        TurquoiseGreen = 29,        // #00FF55
        DarkTurquoiseGreen = 30,    // #00591D
        DimTurquoiseGreen = 31,     // #001F12

        // Row 8: Cyan-Green (Seafoam)
        LightSeafoam = 32,          // #4CFFB7
        Seafoam = 33,               // #00FF99
        DarkSeafoam = 34,           // #005935
        DimSeafoam = 35,            // #001912

        // Row 9: Aquamarine
        LightAquamarine = 36,       // #4CFFD2
        Aquamarine = 37,            // #00FFCC
        DarkAquamarine = 38,        // #005949
        DimAquamarine = 39,         // #001919

        // Row 10: Cyan
        LightCyan = 40,             // #4CFFFF
        Cyan = 41,                  // #00FFFF
        DarkCyan = 42,              // #005959
        DimCyan = 43,               // #001919

        // Row 11: Sky Blue
        LightSkyBlue = 44,          // #4CC3FF
        SkyBlue = 45,               // #0099FF
        DarkSkyBlue = 46,           // #004152
        DimSkyBlue = 47,            // #001019

        // Row 12: Azure Blue
        LightAzure = 48,            // #4C88FF
        Azure = 49,                 // #0055FF
        DarkAzure = 50,             // #001D59
        DimAzure = 51,              // #000819

        // Row 13: Blue
        LightBlue = 52,             // #4C4CFF
        Blue = 53,                  // #0000FF
        DarkBlue = 54,              // #000059
        DimBlue = 55,               // #000019

        // Row 14: Violet
        LightViolet = 56,           // #874CFF
        Violet = 57,                // #5400FF
        DarkViolet = 58,            // #190059
        DimViolet = 59,             // #0F0030

        // Row 15: Purple
        LightPurple = 60,           // #BC4CFF
        Purple = 61,                // #9000FF
        DarkPurple = 62,            // #320059
        DimPurple = 63,             // #190026

        // Row 16: Magenta
        LightMagenta = 64,          // #FF4CFF
        Magenta = 65,               // #FF00FF
        DarkMagenta = 66,           // #590059
        DimMagenta = 67,            // #190019

        // Row 17: Pink (Magenta-Red)
        LightPink = 68,             // #FF4C87
        Pink = 69,                  // #FF0054
        DarkPink = 70,              // #59001D
        DimPink = 71,               // #220013

        // Row 18: Warm accent colors
        OrangeRed = 72,             // #FF1500
        BurntOrange = 73,           // #993500
        Olive = 74,                 // #795100
        DarkOlive = 75,             // #436400

        // Row 19: Cool accent colors
        ForestGreen = 76,           // #033900
        Teal = 77,                  // #005735
        DeepTeal = 78,              // #00547F
        Navy = 79,                  // #0000FF

        // Row 20: Deep cool tones
        DarkSlateBlue = 80,         // #00454F
        Indigo = 81,                // #2500CC
        DeepViolet = 82,            // #7F00FF
        Fuchsia = 83,               // #B4177F

        // Row 21: Warm earth tones
        Maroon = 84,                // #902100
        Rust = 85,                  // #8C3400
        Amber = 86,                 // #E97400
        Gold = 87,                  // #B3B300

        // Row 22: Yellow-Green tones
        YellowGreen = 88,           // #7FB300
        LimeGreen = 89,             // #4B9900
        GrassGreen = 90,            // #247F00
        DarkForestGreen = 91,       // #0F5C00

        // Row 23: Teal-Blue tones
        SeaGreen = 92,              // #007F46
        DarkCyanGreen = 93,         // #007F7F
        SteelBlue = 94,             // #004CCC
        RoyalBlue = 95,             // #1841FF

        // Row 24: Purple-Pink pastels
        SlateBlue = 96,             // #6050FF
        Orchid = 97,                // #BC7FE1
        LightOrchid = 98,           // #D8A6CF
        PalePink = 99,              // #E6AAAC

        // Row 25: Warm pastels
        Beige = 100,                // #D0C9A6
        PaleGold = 101,             // #CBAB4F
        Khaki = 102,                // #93934F
        OliveGreen = 103,           // #6B6B4B

        // Row 26: Cool pastels
        SageGreen = 104,            // #4B6B4F
        GrayTeal = 105,             // #4B7F87
        GrayBlue = 106,             // #6B6BD1
        Periwinkle = 107,           // #9FA1ED

        // Row 27: Muted pastels
        LavenderGray = 108,         // #97A4E1
        MutedViolet = 109,          // #8181C9
        MutedPink = 110,            // #A797A3
        DustyRose = 111,            // #BAA4AC

        // Row 28: Earth tones
        Tan = 112,                  // #968476
        DarkTan = 113,              // #9A8D64
        DarkKhaki = 114,            // #97974C
        MutedOlive = 115,           // #737263

        // Row 29: Gray tones
        GrayGreen = 116,            // #636C58
        SlateGray = 117,            // #636C7F
        CoolGray = 118,             // #6B6BAB
        LightSlateGray = 119,       // #8C8CAF

        // Row 30: Bright accents
        BrightRed = 120,            // #FF0000
        BrightOrange = 121,         // #EB7F00
        BrightYellow = 122,         // #CFBF00
        BrightChartreuse = 123,     // #8EE100

        // Row 31: Bright accents (continued)
        BrightGreen = 124,          // #00E133
        BrightTurquoise = 125,      // #00D2A8
        BrightCyan = 126,           // #00A3E0
        BrightSkyBlue = 127,        // #007FFF
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
    private bool _useGenericMode = true;
}