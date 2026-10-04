using NAudio.Midi;
using T3.IoServices;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("a59c583b-dbc2-495c-a9b1-e64fc1e5d532")]
internal sealed class MidiControlOutput : Instance<MidiControlOutput>, MidiConnectionManager.IMidiConsumer, ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "2BDE4FD3-E74E-49C1-9FE5-867060067566")]
    public readonly Slot<Command> Result = new();

    public MidiControlOutput()
    {
        Result.UpdateAction = Update;
    }

    private bool _initialized;
    protected override void Dispose(bool isDisposing)
    {
        if(!isDisposing) return;

        if (_initialized)
        {
            MidiConnectionManager.UnregisterConsumer(this);
        }
    }
    private void Update(EvaluationContext context)
    {
        var sendMode = SendMode.GetEnumValue<SendModes>(context);
        var triggerActive = TriggerSend.GetValue(context);
        var ccOrPressure = CCorPressure.GetEnumValue<DataTypes>(context);
        var deviceName = Device.GetValue(context);
        var foundDevice = false;
        var controller = ControllerNumber.GetValue(context).Clamp(0, 127);


        var useFloat = UseValueFloat.GetValue(context);
        int intValue;
        if (!useFloat)
        {
            intValue = Value.GetValue(context).Clamp(0, 127);
        }
        else
        {
            intValue = (int)(ValueFloat.GetValue(context).Clamp(0, 1) * 127);
        }


        if(!_initialized)
        {
            MidiConnectionManager.RegisterConsumer(this);
            _initialized = true;
        }

        var triggerJustActivated = false;

        if (triggerActive != _triggered)
        {
            if (triggerActive)
            {
                triggerJustActivated = true;
            }
            _triggered = triggerActive;
        }
            
        foreach (var (m, device) in MidiConnectionManager.MidiOutsWithDevices)
        {
            if (device.ProductName != deviceName)
                continue;
                
            var channel = ChannelNumber.GetValue(context).Clamp(1,16);
            try
            {
                MidiEvent midiEvent = null;
                switch (ccOrPressure)
                {
                    case DataTypes.ContinuosController_CC:
                        switch (sendMode)
                        {
                            case SendModes.SendWhenTriggered:
                                if (triggerJustActivated)
                                {
                                    midiEvent = new ControlChangeEvent(0, channel, (MidiController)controller, intValue);
                                }
                                break;
                            case SendModes.SendContinuously:
                                midiEvent = new ControlChangeEvent(0, channel, (MidiController)controller, intValue);
                                break;
                        }
                        break;

                    case DataTypes.ChannelPressure:

                        switch (sendMode)
                        {
                            case SendModes.SendWhenTriggered:
                                if (triggerJustActivated)
                                {
                                    midiEvent = new ChannelAfterTouchEvent(0, channel, intValue);
                                }
                                break;
                            case SendModes.SendContinuously:
                                midiEvent = new ChannelAfterTouchEvent(0, channel, intValue);
                                break;
                        }
                        break;
                }

                if (midiEvent != null)
                    m.Send(midiEvent.GetAsShortMessage());
                    
                //Log.Debug("Sending MidiTo " + device.Manufacturer + " " + device.ProductName, this);
                foundDevice = true;
                break;
            }
            catch (Exception e)
            {
                _lastErrorMessage = $"Failed to send midi to {deviceName}: " + e.Message;
                Log.Warning(_lastErrorMessage, this);
            }
                
        }
        _lastErrorMessage = !foundDevice ? $"Can't find MidiDevice {deviceName}" : null;
    }
        
    //private double _lastNoteOnTime;

    private bool _triggered;

    //private static int GetMicrosecondsPerQuarterNoteFromBpm(double bpm)
    //{
    //    var ms = 600000 / bpm;
    //    return (int)ms;
    //}
    private enum SendModes
    {
        SendContinuously,
        SendWhenTriggered
    }
    private enum DataTypes
    {
        ContinuosController_CC,
        ChannelPressure
    }


    #region device dropdown

    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return Device.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != Device.Id)
        {
            yield return "undefined";
            yield break;
        }
            
        foreach (var device in MidiConnectionManager.MidiOutsWithDevices.Values)
        {
            yield return device.ProductName;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        Log.Debug($"Got {selected}", this);
        Device.SetTypedInputValue(selected);
    }
    #endregion
        
    #region Implement statuslevel
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage;
    }

    // We don't actually receive midi in this operator, those methods can remain empty, we just want the MIDI connection thread up
    public void MessageReceivedHandler(object sender, MidiInMessageEventArgs msg) {}

    public void ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg) {}

    public void OnSettingsChanged() {}

    private string _lastErrorMessage;
    #endregion

    [Input(Guid = "B8612A7D-CB19-4203-B5D0-A44CA71F5477", MappedType = typeof(SendModes))]
    public readonly InputSlot<int> SendMode = new();

    [Input(Guid = "5188CC2F-723C-497C-BFB8-1B00A3006327")]
    public readonly InputSlot<bool> TriggerSend = new ();

    [Input(Guid = "DC2F8501-2889-4949-9866-3C0EB43CD061", MappedType = typeof(DataTypes))]
    public readonly InputSlot<int> CCorPressure = new();

    [Input(Guid = "AFF6A747-7C3D-4CEF-A5B7-DAF9A60FCBA0")]
    public readonly InputSlot<string> Device = new ();
        
    [Input(Guid = "B8C8F9D2-4EC9-4351-82AE-A17140BBE484")]
    public readonly InputSlot<int> ChannelNumber = new ();

    [Input(Guid = "083E3A73-4539-408E-A91A-1739CB4F3CC6")]
    public readonly InputSlot<int> ControllerNumber = new();

    [Input(Guid = "57096500-4EE4-4221-A7E7-1D2A065D01C8")]
    public readonly InputSlot<int> Value = new ();

    [Input(Guid = "F558C83A-2BA1-4CFE-BBF4-F5CBFDF68C73")]
    public readonly InputSlot<bool> UseValueFloat = new();

    [Input(Guid = "5734D4C9-1D84-4559-91D6-6611F0EA1658")]
    public readonly InputSlot<float> ValueFloat = new ();
}