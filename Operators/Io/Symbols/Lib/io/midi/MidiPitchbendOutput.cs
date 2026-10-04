using NAudio.Midi;
using T3.IoServices;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("01d33d21-d75e-4c22-bfe6-088e1ee4a5e8")]
internal sealed class MidiPitchbendOutput : Instance<MidiPitchbendOutput>, MidiConnectionManager.IMidiConsumer, ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "402D1D43-5EAB-46F0-88C9-2C978E4223E8")]
    public readonly Slot<Command> Result = new();

    public MidiPitchbendOutput()
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
        var deviceName = Device.GetValue(context);
        var foundDevice = false;

        var useFloat = UsePitchFloat.GetValue(context);
        int intPitch;
        if (!useFloat)
        {
            intPitch = Pitch.GetValue(context).Clamp(-8192, 8191) + 8192;
        }
        else
        {
            intPitch = (int)((PitchFloat.GetValue(context).Clamp(-1, 1) * 8192.0).Clamp(-8192, 8191) + 8192);
        }
        var sendMode = SendMode.GetEnumValue<SendModes>(context);
        var triggerActive = TriggerSend.GetValue(context);

        var triggerJustActivated = false;

        if(!_initialized)
        {
            MidiConnectionManager.RegisterConsumer(this);
            _initialized = true;
        }

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
                switch (sendMode)
                {
                    case SendModes.SendWhenTriggered:
                        if (triggerJustActivated)
                        {
                            midiEvent = new PitchWheelChangeEvent(0, channel, intPitch);
                        }
                        break;
                    case SendModes.SendContinuously:
                        midiEvent = new PitchWheelChangeEvent(0, channel, intPitch);
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

    [Input(Guid = "5B25A41F-7609-4E19-98A2-1ECFAEB397BB", MappedType = typeof(SendModes))]
    public readonly InputSlot<int> SendMode = new();

    [Input(Guid = "E8665F67-9760-4289-AE2F-13665F63B4D8")]
    public readonly InputSlot<bool> TriggerSend = new ();

    [Input(Guid = "93868179-7D95-46C3-BD2C-8D6039AFEE69")]
    public readonly InputSlot<string> Device = new ();
        
    [Input(Guid = "94F63571-52F4-45B8-A784-5556B4455F6A")]
    public readonly InputSlot<int> ChannelNumber = new ();

    [Input(Guid = "B9982ED7-C481-4508-A61E-229EAF5E960E")]
    public readonly InputSlot<int> Pitch = new ();

    [Input(Guid = "068A8EF1-9AA4-4E67-9700-5138358FA0B9")]
    public readonly InputSlot<bool> UsePitchFloat = new();

    [Input(Guid = "E92B7F08-18C6-402B-A4DC-BF553F881593")]
    public readonly InputSlot<float> PitchFloat = new ();
}