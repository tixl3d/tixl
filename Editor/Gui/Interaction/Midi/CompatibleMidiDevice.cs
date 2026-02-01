using NAudio;
using NAudio.Midi;
using Operators.Utils;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Combines midi signals related to Variations into triggers and invokes matching <see cref="CommandTriggerCombination"/>s.
/// Allow to update the status of midi devices, e.g. for controlling LEDs to indicate available or active variations.
/// </summary>
/// <remarks>
/// This is NOT related to the MidiInput operator: Both are registered as independent <see cref="MidiConnectionManager.IMidiConsumer"/>
/// and handle their events individually.
/// </remarks>
public abstract class CompatibleMidiDevice : MidiConnectionManager.IMidiConsumer, IDisposable
{
    internal void Initialize(MidiIn midiIn, MidiOut midiOut)
    {
        _midiInputConnection = midiIn;
        MidiOutConnection = midiOut;
        
        // Store the device name for control mode management
        var deviceInfo = MidiConnectionManager.GetDescriptionForMidiIn(midiIn);
        _deviceProductName = deviceInfo.ProductName;
        
        MidiConnectionManager.RegisterConsumer(this);
    }

    /// <summary>
    /// Sets whether this device should be in control mode (blocking passthrough) or passthrough mode.
    /// In control mode: MIDI messages are consumed by the compatible device and not passed to graph operators.
    /// In passthrough mode: MIDI messages are passed through to graph operators, editor controls are disabled.
    /// </summary>
    protected void SetControlMode(bool controlMode)
    {
        var wasInControlMode = IsInControlMode;
        IsInControlMode = controlMode;
        
        if (!string.IsNullOrEmpty(_deviceProductName))
        {
            MidiConnectionManager.SetDeviceControlMode(_deviceProductName, controlMode);
        }
        
        // Notify derived classes when mode changes
        if (wasInControlMode != controlMode)
        {
            OnControlModeChanged(controlMode);
        }
    }

    /// <summary>
    /// Called when control mode changes. Override to reset LEDs or reinitialize device.
    /// </summary>
    protected virtual void OnControlModeChanged(bool isNowInControlMode)
    {
        if (!isNowInControlMode)
        {
            // Switching to passthrough mode - clear all cached LED colors so they get reset
            ClearLedCache();
        }
    }

    /// <summary>
    /// Clears the cached LED colors. Called when switching to passthrough mode.
    /// </summary>
    private static void ClearLedCache()
    {
        for (var i = 0; i < CacheControllerColors.Length; i++)
        {
            CacheControllerColors[i] = -1;
        }
    }

    /// <summary>
    /// Gets whether this device is in control mode (blocking passthrough).
    /// </summary>
    protected bool IsInControlMode { get; private set; } = true;

    private string _deviceProductName;

    /// <summary>
    /// Depending on various hotkeys a device can be in different input modes.
    /// This allows actions with the button combinations.
    /// </summary>
    [Flags]
    public enum InputModes
    {
        Default = 1 << 1,
        Delete = 1 << 2,
        Save = 1 << 3,
        BlendTo = 1 << 4,
        None = 0,
    }

    protected InputModes ActiveMode = InputModes.Default;

    protected abstract void UpdateVariationVisualization();
    
    /// <summary>
    /// Called when in passthrough mode to clear/reset device LEDs.
    /// Override in derived classes to send device-specific reset commands.
    /// </summary>
    protected virtual void ClearDeviceLeds() { }

    public void Update()
    {
        if (IsInControlMode)
        {
            // Control mode: normal operation - update LEDs and process all signals
            UpdateVariationVisualization();
        }
        else
        {
            // Passthrough mode: clear LEDs and only process mode-switching signals
            ClearDeviceLeds();
        }
        
        CombineButtonSignals();
        ProcessLastSignals();
        _hasNewMessages = false;
    }

    private void ProcessLastSignals()
    {
        if (!_hasNewMessages)
            return;
        
        ControlChangeSignal[] controlChangeSignals;
        lock (_controlSignalsSinceLastUpdate)
        {
            controlChangeSignals = _controlSignalsSinceLastUpdate.ToArray();
            _controlSignalsSinceLastUpdate.Clear();
        }

        // Only process control changes in control mode
        if (IsInControlMode && controlChangeSignals.Length != 0)
        {
            foreach (var ctc in CommandTriggerCombinations)
            {
                ctc.InvokeMatchingControlCommands(controlChangeSignals, ActiveMode);
            }
        }


        if (_combinedButtonSignals.Count == 0)
            return;

        // Log.Debug($"Processing {_combinedButtonSignals.Count} button signal(s), ActiveMode={ActiveMode}, ControlMode={_isInControlMode}");
        
        var releasedMode = InputModes.None;

        // Mode buttons should ALWAYS be processed (even in passthrough mode) so user can switch back
        if (ModeButtons != null)
        {
            foreach (var modeButton in ModeButtons)
            {
                var matchingSignal = _combinedButtonSignals.Values.SingleOrDefault(s => modeButton.ButtonRange.IncludesButtonIndex(s.ButtonId));
                if (matchingSignal == null)
                    continue;

                if (matchingSignal.State == ButtonSignal.States.JustPressed)
                {
                    if (ActiveMode == InputModes.Default)
                    {
                        // Log.Debug($"Mode changed to {modeButton.Mode} (button {matchingSignal.ButtonId})");
                        ActiveMode = modeButton.Mode;
                    }
                }
                else if (matchingSignal.State == ButtonSignal.States.Released && ActiveMode == modeButton.Mode)
                {
                    // Log.Debug($"Mode released from {modeButton.Mode} back to Default");
                    releasedMode = modeButton.Mode;
                    ActiveMode = InputModes.Default;
                }
            }
        }

        if (CommandTriggerCombinations == null)
            return;

        var isAnyButtonPressed = _combinedButtonSignals.Values.Any(signal => (signal.State == ButtonSignal.States.JustPressed
                                                                              || signal.State == ButtonSignal.States.Hold));

        foreach (var ctc in CommandTriggerCombinations)
        {
            // In passthrough, ignore all buttons except mode-switches (InputModes.Save / Shift).
            if (!IsInControlMode && ctc.RequiredInputMode != InputModes.Save)
                continue;
                
            ctc.InvokeMatchingButtonCommands(_combinedButtonSignals.Values.ToList(), ActiveMode, releasedMode);
        }

        if (!isAnyButtonPressed)
        {
            _combinedButtonSignals.Clear();
        }
    }

    public void Dispose()
    {
        // Clear control mode when device is disposed so messages pass through again
        SetControlMode(false);
        MidiConnectionManager.UnregisterConsumer(this);
    }

    protected List<CommandTriggerCombination> CommandTriggerCombinations;
    protected List<ModeButton> ModeButtons;
    private bool _hasNewMessages;

    // ------------------------------------------------------------------------------------
    #region Process button Signals
    /// <summary>
    /// Combines press/hold/release signals into states like JustPressed and Hold than are
    /// later used to check for actions triggered by button combinations. 
    /// </summary>
    private void CombineButtonSignals()
    {
        if (!_hasNewMessages)
            return;
        
        lock (_buttonSignalsSinceLastUpdate)
        {
            if (_buttonSignalsSinceLastUpdate.Count > 0)
            {
                // Log.Debug($"CombineButtonSignals: {_buttonSignalsSinceLastUpdate.Count} new signal(s) to process");
            }
            
            foreach (var earlierSignal in _combinedButtonSignals.Values)
            {
                if (earlierSignal.State == ButtonSignal.States.JustPressed)
                    earlierSignal.State = ButtonSignal.States.Hold;
            }

            foreach (var newSignal in _buttonSignalsSinceLastUpdate)
            {
                // Log.Debug($"CombineButtonSignals: Processing signal ButtonId={newSignal.ButtonId}, State={newSignal.State}");
                if (_combinedButtonSignals.TryGetValue(newSignal.ButtonId, out var earlierSignal))
                {
                    earlierSignal.State = newSignal.State;
                }
                else
                {
                    _combinedButtonSignals[newSignal.ButtonId] = newSignal;
                }
            }

            _buttonSignalsSinceLastUpdate.Clear();
        }
    }

    void MidiConnectionManager.IMidiConsumer.OnSettingsChanged()
    {
    }

    void MidiConnectionManager.IMidiConsumer.MessageReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
        if (sender is not MidiIn midiIn || msg.MidiEvent == null)
            return;

        if (midiIn != _midiInputConnection)
            return;

        if (msg.MidiEvent == null)
            return;

        switch (msg.MidiEvent.CommandCode)
        {
            case MidiCommandCode.NoteOff:
            case MidiCommandCode.NoteOn:
                if (msg.MidiEvent is NoteEvent noteEvent)
                {
                    var state = msg.MidiEvent.CommandCode == MidiCommandCode.NoteOn
                        ? ButtonSignal.States.JustPressed
                        : ButtonSignal.States.Released;
                    
                    // Allow device-specific mapping from channel/note to button ID
                    var buttonId = ConvertNoteToButtonId(noteEvent.Channel, noteEvent.NoteNumber);
                    
                    // Log.Debug($"MIDI Button: Note={noteEvent.NoteNumber}, Channel={noteEvent.Channel}, ButtonId={buttonId}, Velocity={noteEvent.Velocity}, State={state}");
                    
                    lock (_buttonSignalsSinceLastUpdate)
                    {
                        _buttonSignalsSinceLastUpdate.Add(new ButtonSignal()
                                                              {
                                                                  Channel = noteEvent.Channel,
                                                                  ButtonId = buttonId,
                                                                  ControllerValue = noteEvent.Velocity,
                                                                  State = state,
                                                              });
                    }
                    _hasNewMessages = true;
                }
                return;

            case MidiCommandCode.ControlChange:
                if (msg.MidiEvent is not ControlChangeEvent controlChangeEvent)
                    return;

                // Note: Debug log removed - too verbose for continuous controllers like crossfaders

                lock (_controlSignalsSinceLastUpdate)
                {
                    _controlSignalsSinceLastUpdate.Add(new ControlChangeSignal()
                                                           {
                                                               Channel = controlChangeEvent.Channel,
                                                               ControllerId = (int)controlChangeEvent.Controller,
                                                               ControllerValue = controlChangeEvent.ControllerValue,
                                                           });
                }
                _hasNewMessages = true;
                return;
        }
    }

    /// <summary>
    /// Converts a MIDI channel and note number to a button ID.
    /// Override this in derived classes for devices that use channel-based button mapping.
    /// </summary>
    protected virtual int ConvertNoteToButtonId(int channel, int noteNumber)
    {
        // Default: just use the note number
        return noteNumber;
    }

    void MidiConnectionManager.IMidiConsumer.ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
    }
    #endregion

    //---------------------------------------------------------------------------------
    #region SendColors
    protected delegate int ComputeColorForIndex(int index);

    protected void UpdateRangeLeds(ButtonRange range, ComputeColorForIndex computeColorForIndex)
    {
        if (MidiOutConnection == null)
            return;
        
        foreach (var buttonIndex in range.Indices())
        {
            var mappedIndex = range.GetMappedIndex(buttonIndex);
            SendColor(MidiOutConnection, buttonIndex, computeColorForIndex(mappedIndex));
        }
    }

    protected virtual void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        if (CacheControllerColors[apcControlIndex] == colorCode)
            return;

        const int defaultChannel = 1;
        var noteOnEvent = new NoteOnEvent(0, defaultChannel, apcControlIndex, colorCode, 50);
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

    protected static readonly int[] CacheControllerColors = Enumerable.Repeat(-1, 256).ToArray();
    #endregion

    /// <summary>
    /// Clears all pending button signals. Call this when a mode switch changes button mappings
    /// to prevent stale signals from blocking subsequent mode switches.
    /// </summary>
    protected void ClearButtonSignals()
    {
        _combinedButtonSignals.Clear();
        lock (_buttonSignalsSinceLastUpdate)
        {
            _buttonSignalsSinceLastUpdate.Clear();
        }
    }

    private readonly Dictionary<int, ButtonSignal> _combinedButtonSignals = new();
    private readonly List<ButtonSignal> _buttonSignalsSinceLastUpdate = new();
    private readonly List<ControlChangeSignal> _controlSignalsSinceLastUpdate = new();
    private MidiIn _midiInputConnection;
    protected MidiOut MidiOutConnection;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class MidiDeviceProductAttribute : Attribute
{
    internal MidiDeviceProductAttribute(string productName)
    {
        ProductNames =  productName.Split(';');
    }

    internal string[] ProductNames { get; }
}