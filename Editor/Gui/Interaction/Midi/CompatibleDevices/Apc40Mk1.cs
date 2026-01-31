using NAudio;
using NAudio.Midi;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

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

                    // Stop blending - press Scene Launch 2 to stop blend operation
                    new CommandTriggerCombination(BlendActions.StopBlendingTowards, InputModes.Default, [SceneLaunch2],
                                                  CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),

                    // Start blend towards - hold Scene Launch 2 + press clip button to start blend
                    new CommandTriggerCombination(BlendActions.StartBlendingTowardsSnapshot, requiredInputMode: InputModes.BlendTo, [SceneTrigger1To40],
                                                  CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                    // Update blend progress with crossfader
                    new CommandTriggerCombination(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, [AbFader],
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
                Log.Debug("APC40 Mk1: Setting GENERIC PASSTHROUGH mode (0x40)");
                _useGenericMode = true;
                SendModeInitSysEx();
                SetControlMode(false);
                break;
                
            case 1: // Record/Arm 2 - Ableton passthrough mode (0x41)
                Log.Debug("APC40 Mk1: Setting ABLETON PASSTHROUGH mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(false);
                break;
                
            case 2: // Record/Arm 3 - Ableton control mode (0x41)
                Log.Debug("APC40 Mk1: Setting ABLETON CONTROL mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(true);
                break;
                
            default:
                Log.Debug($"APC40 Mk1: Ignoring mode switch for index {index}");
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
        Log.Debug($"APC40 Mk1: Sending mode SysEx (0x{modeIdentifier:X2})...");
        
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
            Log.Debug($"APC40 Mk1: Mode switch complete (0x{modeIdentifier:X2})");
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
        
        // Reset all cache entries
        for (var i = 0; i < CacheControllerColors.Length; i++)
        {
            CacheControllerColors[i] = -1;
        }
        
        // Clear clip grid using Generic mode mapping (Notes 0-39 on Channel 1)
        foreach (var note in GenericClipGridNotes.Indices())
        {
            var evt = new NoteOnEvent(0, MidiChannels1To8.StartIndex, note, 0, 0);
            try { MidiOutConnection.Send(evt.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to clear LED (Generic grid note {note}): {e.Message}"); }
        }
        
        // Clear clip grid using Ableton mode mapping (Notes 53-57 on Channels 1-8)
        foreach (var note in AbletonClipGridNotes.Indices())
        {
            foreach (var ch in MidiChannels1To8.Indices())
            {
                var evt = new NoteOnEvent(0, ch, note, 0, 0);
                try { MidiOutConnection.Send(evt.GetAsShortMessage()); }
                catch (Exception e) { Log.Warning($"Failed to clear LED (Ableton grid note {note}, ch {ch}): {e.Message}"); }
            }
        }
        
        // Clear scene launch LEDs
        foreach (var note in SceneLaunchNotes.Indices())
        {
            var evt = new NoteOnEvent(0, MidiChannels1To8.StartIndex, note, 0, 0);
            try { MidiOutConnection.Send(evt.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to clear LED (Scene launch note {note}): {e.Message}"); }
        }
        
        // Clear Record/Arm row LEDs for BOTH modes
        // Generic mode: Notes 48-55 on Channel 1
        foreach (var note in GenericRecordArmNotes.Indices())
        {
            var evt = new NoteOnEvent(0, MidiChannels1To8.StartIndex, note, 0, 0);
            try { MidiOutConnection.Send(evt.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to clear LED (Generic Record/Arm note {note}): {e.Message}"); }
        }
        // Ableton mode: Note 48 on Channels 1-8
        foreach (var ch in MidiChannels1To8.Indices())
        {
            var evt = new NoteOnEvent(0, ch, AbletonRecordArmNote, 0, 0);
            try { MidiOutConnection.Send(evt.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to clear LED (Ableton Record/Arm ch {ch}): {e.Message}"); }
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
    /// 
    /// Mode 1 (Record/Arm 1): Generic passthrough (0x40) - DEFAULT
    /// Mode 2 (Record/Arm 2): Ableton passthrough (0x41)
    /// Mode 3 (Record/Arm 3): Ableton control (0x41)
    /// 
    /// LED display is on the Record/Arm row (bottom row) to be consistent across modes:
    /// - Generic mode (0x40): Notes 48-55 on Channel 1 (Record Arm row)
    /// - Ableton mode (0x41): Note 48 on Channels 1-8 (Record Arm row)
    /// </summary>
    private void UpdateRecordArmModeLeds()
    {
        if (MidiOutConnection == null)
            return;

        // Record/Arm 1 - Green when in Generic passthrough mode (0x40)
        var genericModeColor = _useGenericMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off;
        
        // Record/Arm 2 - Green when in Ableton passthrough mode (0x41, not control mode)
        var abletonPassthroughColor = !_useGenericMode && !IsInControlMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off;
        
        // Record/Arm 3 - Green when in Ableton control mode (0x41, control mode)
        var abletonControlColor = !_useGenericMode && IsInControlMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off;

        if (_useGenericMode)
        {
            // Generic mode: Record Arm uses Notes 48-55 on Channel 1
            // Record/Arm 1 = Note 48, Record/Arm 2 = Note 49, Record/Arm 3 = Note 50
            var genericModeEvent = new NoteOnEvent(0, MidiChannels1To8.StartIndex, GenericRecordArmNotes.StartIndex, (int)genericModeColor, 0);
            try { MidiOutConnection.Send(genericModeEvent.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to set Generic Mode LED: {e.Message}"); }
            
            var abletonPassthroughEvent = new NoteOnEvent(0, MidiChannels1To8.StartIndex, GenericRecordArmNotes.StartIndex + 1, (int)abletonPassthroughColor, 0);
            try { MidiOutConnection.Send(abletonPassthroughEvent.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to set Ableton Passthrough LED: {e.Message}"); }
            
            var abletonControlEvent = new NoteOnEvent(0, MidiChannels1To8.StartIndex, GenericRecordArmNotes.StartIndex + 2, (int)abletonControlColor, 0);
            try { MidiOutConnection.Send(abletonControlEvent.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to set Ableton Control LED: {e.Message}"); }
        }
        else
        {
            // Ableton mode: Record Arm uses Note 48 on Channels 1-8
            // Record/Arm 1 = Note 48 Ch1, Record/Arm 2 = Note 48 Ch2, Record/Arm 3 = Note 48 Ch3
            var genericModeEvent = new NoteOnEvent(0, MidiChannels1To8.StartIndex, AbletonRecordArmNote, (int)genericModeColor, 0);
            try { MidiOutConnection.Send(genericModeEvent.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to set Generic Mode LED: {e.Message}"); }
            
            var abletonPassthroughEvent = new NoteOnEvent(0, MidiChannels1To8.StartIndex + 1, AbletonRecordArmNote, (int)abletonPassthroughColor, 0);
            try { MidiOutConnection.Send(abletonPassthroughEvent.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to set Ableton Passthrough LED: {e.Message}"); }
            
            var abletonControlEvent = new NoteOnEvent(0, MidiChannels1To8.StartIndex + 2, AbletonRecordArmNote, (int)abletonControlColor, 0);
            try { MidiOutConnection.Send(abletonControlEvent.GetAsShortMessage()); }
            catch (Exception e) { Log.Warning($"Failed to set Ableton Control LED: {e.Message}"); }
        }
    }

    /// <summary>
    /// Clears all LEDs on the device when in passthrough mode.
    /// Still shows mode highlighting when Shift is held.
    /// </summary>
    protected override void ClearDeviceLeds()
    {
        if (MidiOutConnection == null)
            return;

        _updateCount++;
        
        // Turn off all clip launch grid LEDs (0-39) or show mode highlight if Shift is held
        for (var i = 0; i < ClipGridSize; i++)
        {
            // Reset cache to force update when flashing
            if (ActiveMode != InputModes.Default)
                CacheControllerColors[i] = -1;
            
            var color = AddModeHighlight(i, (int)Apc40Mk1Colors.Off);
            SendColor(MidiOutConnection, i, color);
        }
        
        // Turn off scene launch LEDs - always off in passthrough mode
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
        {
            SendModeInitSysEx();
        }

        // Update clip launch button LEDs (5x8 grid)
        UpdateRangeLeds(SceneTrigger1To40,
                        mappedIndex =>
                        {
                            var color = Apc40Mk1Colors.Off;

                            // Get variation snapshot for this index
                            if (!SymbolVariationPool.TryGetSnapshot(mappedIndex, out var v)) return AddModeHighlight(mappedIndex, (int)color);
                            
                            // Check if this is the current blend target
                            var isBlendTarget = BlendActions.BlendTowardsIndex == mappedIndex;
                                
                            // Determine color based on state, with blend target shown as orange
                            // Priority: Active (red) > BlendTarget (orange) > other states
                            color = v.State switch
                                        {
                                            Variation.States.Active    => Apc40Mk1Colors.Red,
                                            Variation.States.Modified  => Apc40Mk1Colors.Orange,
                                            Variation.States.IsBlended => Apc40Mk1Colors.OrangeBlinking,
                                            Variation.States.InActive  => isBlendTarget ? Apc40Mk1Colors.OrangeBlinking : Apc40Mk1Colors.Green,
                                            Variation.States.Undefined => Apc40Mk1Colors.Off,
                                            _                          => color
                                        };

                            return AddModeHighlight(mappedIndex, (int)color);
                        });

        // Update scene launch button LEDs to show current mode - only in Ableton control mode (mode 3)
        // Not in Generic passthrough (mode 1) or Ableton passthrough (mode 2)
        if (IsInControlMode && !_useGenericMode)
        {
            UpdateSceneLaunchLeds();
        }
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
        SendColor(MidiOutConnection, SceneLaunchNotes.StartIndex, (int)deleteModeColor);

        // Scene Launch 2 (BlendTo mode indicator)
        var blendModeColor = ActiveMode == InputModes.BlendTo 
            ? Apc40Mk1Colors.OrangeBlinking 
            : Apc40Mk1Colors.Orange;
        SendColor(MidiOutConnection, SceneLaunchNotes.StartIndex + 1, (int)blendModeColor);

        // Scene Launch 3-5 can show other states (currently off)
        SendColor(MidiOutConnection, SceneLaunchNotes.StartIndex + 2, (int)Apc40Mk1Colors.Off);
        SendColor(MidiOutConnection, SceneLaunchNotes.StartIndex + 3, (int)Apc40Mk1Colors.Off);
        SendColor(MidiOutConnection, SceneLaunchNotes.StartIndex + 4, (int)Apc40Mk1Colors.Off);
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
    /// Override SendColor to use the APC40 Mk1 specific channel mapping for LED control.
    /// 
    /// The mapping differs between Generic Mode (0x40) and Ableton Live Mode (0x41):
    /// 
    /// GENERIC MODE (0x40):
    /// - Clip Launch grid (indices 0-39): Notes 0-39 on Channel 1
    /// - Other buttons: Channel 1 with note = button index
    /// 
    /// ABLETON LIVE MODE (0x41):
    /// - Clip Launch grid (indices 0-39): Uses Notes 53-57 on Channels 1-8
    ///   index = ((note - 53) * 8) + (channel - 1), so:
    ///   note = (index / 8) + 53, channel = (index % 8) + 1
    /// - Other buttons: Channel 1 with note = button index
    /// </summary>
    protected override void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        // Quick guard: ensure index is within cache bounds
        if (apcControlIndex < 0 || apcControlIndex >= CacheControllerColors.Length)
            return;

        if (CacheControllerColors[apcControlIndex] == colorCode)
            return;

        int channel;
        int noteNumber;

        // Clip launch grid buttons (0-39) need mode-specific mapping
        if (apcControlIndex < ClipGridSize)
        {
            if (_useGenericMode)
            {
                // Generic mode: Notes 0-39 on Channel 1
                channel = MidiChannels1To8.StartIndex;
                noteNumber = apcControlIndex;
            }
            else
            {
                // Ableton mode: index -> (note,channel)
                int row = apcControlIndex / AbletonClipGridColumns;
                int col = apcControlIndex % AbletonClipGridColumns;
                noteNumber = row + AbletonClipGridNotes.StartIndex;
                channel = col + MidiChannels1To8.StartIndex;
            }
        }
        else
        {
            // Scene launch and other non-grid buttons use channel 1 and note = index
            channel = MidiChannels1To8.StartIndex;
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
    /// The mapping differs between Generic Mode (0x40) and Ableton Live Mode (0x41):
    /// 
    /// GENERIC MODE (0x40):
    /// - Clip Launch grid: Notes 0-39 on Channel 1
    /// - Track Select buttons: Notes 58-65 on Channel 1
    /// - Shift button: Note 98 on Channel 1
    /// 
    /// ABLETON LIVE MODE (0x41):
    /// - Clip Launch grid: Notes 53-57 (rows 1-5) on Channels 1-8 (tracks/columns)
    /// - Track Select buttons: Note 51 on Channels 1-8
    /// - Shift button: Note 98 on Channel 1
    /// </summary>
    protected override int ConvertNoteToButtonId(int channel, int noteNumber)
    {
        // Shift button is the same in both modes - handle it first
        // This ensures Shift + button combinations work in any mode
        if (noteNumber == ShiftButtonNote && channel == MidiChannels1To8.StartIndex)
        {
            Log.Debug($"ConvertNoteToButtonId: Shift button Note={noteNumber}, Channel={channel} -> ButtonId={ShiftButtonNote}");
            return ShiftButtonNote;
        }
        
        // ===== CLIP LAUNCH GRID - check FIRST to avoid conflicts with Record/Arm fallback =====
        // In Ableton mode, clip grid uses notes 53-57 which would otherwise be caught by Record/Arm fallback
        
        if (_useGenericMode)
        {
            // GENERIC MODE (0x40): Clip launch grid uses notes 0-39 on channel 1
            if (GenericClipGridNotes.IncludesButtonIndex(noteNumber) && channel == MidiChannels1To8.StartIndex)
            {
                Log.Debug($"ConvertNoteToButtonId [Generic]: Clip grid Note={noteNumber}, Channel={channel} -> ButtonId={noteNumber}");
                return noteNumber;
            }
        }
        else
        {
            // ABLETON LIVE MODE (0x41): Clip launch grid uses notes 53-57 on channels 1-8
            // This creates a 5 row x 8 column grid (40 buttons)
            if (AbletonClipGridNotes.IncludesButtonIndex(noteNumber) && MidiChannels1To8.IncludesButtonIndex(channel))
            {
                // Convert to linear index 0-39
                // Note 53 on Ch1 = index 0, Note 53 on Ch2 = index 1, ..., Note 53 on Ch8 = index 7
                // Note 54 on Ch1 = index 8, Note 54 on Ch2 = index 9, ..., Note 54 on Ch8 = index 15
                var row = AbletonClipGridNotes.GetMappedIndex(noteNumber);
                var col = MidiChannels1To8.GetMappedIndex(channel);
                var index = (row * AbletonClipGridColumns) + col;
                Log.Debug($"ConvertNoteToButtonId [Ableton]: Clip grid Note={noteNumber}, Channel={channel} -> row={row}, col={col}, ButtonId={index}");
                return index;
            }
        }
        
        // ===== RECORD/ARM BUTTONS - for mode switching =====
        // Record/Arm buttons need to work in BOTH modes so we can switch between them
        // In Ableton mode: Note 48 on Channels 1-8
        // In Generic mode: Notes 48-55 on Channel 1
        
        // Ableton mode Record/Arm: Note 48 on Channels 1-8
        if (noteNumber == AbletonRecordArmNote && MidiChannels1To8.IncludesButtonIndex(channel))
        {
            // Ableton-style per-column mapping
            var buttonId = RecordArmBaseId + MidiChannels1To8.GetMappedIndex(channel);
            Log.Debug($"ConvertNoteToButtonId: Record/Arm (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
            return buttonId;
        }

        switch (_useGenericMode)
        {
            // Generic mode Record/Arm: Notes 49-55 on Channel 1 (note 48 handled above)
            case true when channel == MidiChannels1To8.StartIndex
                           && GenericRecordArmNotes.IncludesButtonIndex(noteNumber) && noteNumber != AbletonRecordArmNote:
            {
                var buttonId = RecordArmBaseId + GenericRecordArmNotes.GetMappedIndex(noteNumber);
                Log.Debug($"ConvertNoteToButtonId: Record/Arm (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                return buttonId;
            }
            // ===== TRACK SELECT BUTTONS =====
            // In Generic mode: Notes 58-65 on Channel 1
            // In Ableton mode: Note 51 on Channels 1-8
            case true when channel == MidiChannels1To8.StartIndex && GenericTrackSelectNotes.IncludesButtonIndex(noteNumber):
            {
                var buttonId = TrackSelectBaseId + GenericTrackSelectNotes.GetMappedIndex(noteNumber);
                Log.Debug($"ConvertNoteToButtonId: Track Select (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                return buttonId;
            }
        }

        if (_useGenericMode || noteNumber != AbletonTrackSelectNote || !MidiChannels1To8.IncludesButtonIndex(channel)) return noteNumber;
        {
            var buttonId = TrackSelectBaseId + MidiChannels1To8.GetMappedIndex(channel);
            Log.Debug($"ConvertNoteToButtonId: Track Select (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
            return buttonId;
        }

        // ===== DEFAULT FALLBACK =====
        // All other buttons use note number directly
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
    private bool _useGenericMode = true; // Default to Generic passthrough mode (0x40)
}
