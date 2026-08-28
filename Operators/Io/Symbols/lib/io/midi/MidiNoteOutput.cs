using NAudio.Midi;
using T3.IoServices;
using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("642cec4f-d4e0-4d0a-8dc3-6ca8339b5f89")]
internal sealed class MidiNoteOutput : Instance<MidiNoteOutput>, MidiConnectionManager.IMidiConsumer, ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "2CB961E5-C188-4190-B02B-753784CAFCA3")]
    public readonly Slot<Command> Result = new();

    public MidiNoteOutput()
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
        var triggerActive = TriggerSend.GetValue(context);
        var sendMode = SendMode.GetEnumValue<SendModes>(context);
        var deviceName = Device.GetValue(context);
        var foundDevice = false;
        var channel = ChannelNumber.GetValue(context).Clamp(1, 16);
        var noteIndex = NoteNumber.GetValue(context).Clamp(0, 127);
        var useFloatVelocity = UseVelocityFloat.GetValue(context);
        var durationInMs = ((int)(DurationInSecs.GetValue(context)*1000)).Clamp(1, 100000);

        int velocity;
        if (useFloatVelocity)
        {
            velocity = (int)(VelocityFloat.GetValue(context).Clamp(0, 1) * 127);
        }
        else
        {
            velocity = Velocity.GetValue(context).Clamp(0, 127);
        }

        var triggerJustActivated = false;
        var triggerJustDeactivated = false;

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
            else
            {
                triggerJustDeactivated = true;
            }

            _triggered = triggerActive;
        }

        var absTime = (long)Playback.RunTimeInSecs * 1000;


        foreach (var (m, device) in MidiConnectionManager.MidiOutsWithDevices)
        {
            if (device.ProductName != deviceName)
                continue;           
                
            try
            {
                MidiEvent midiEvent =null;
                switch (sendMode)
                {
                    case SendModes.Note_WhileTriggered:
                        if (triggerJustActivated)
                        {
                            var noteOnEvent = new NoteOnEvent(0, channel, noteIndex, velocity, durationInMs);
                            midiEvent = noteOnEvent;
                            _offEvent = noteOnEvent.OffEvent;
                        }
                        else if (triggerJustDeactivated)
                        {
                            midiEvent = _offEvent;
                            _offEvent = null;
                        }
                        break;
                        
                    case SendModes.Note_FixedDuration:
                        if (triggerJustActivated)
                        {
                            if(_offEvent != null) 
                            {
                                m.Send(_offEvent.GetAsShortMessage());
                                _offEvent = null;
                            }
                            var noteOnEvent = new NoteOnEvent(0, channel, noteIndex, velocity, durationInMs);
                            midiEvent = noteOnEvent;
                            _lastNoteOnTime = absTime;
                            _offEvent = noteOnEvent.OffEvent;
                        }
                        else if (absTime - _lastNoteOnTime > durationInMs)
                        {
                            midiEvent = _offEvent;
                            _offEvent = null;
                        }
                        break;
                        
                }
                if(midiEvent != null)
                    m.Send(midiEvent.GetAsShortMessage());
                    
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
        
    private double _lastNoteOnTime;

    private static int GetMicrosecondsPerQuarterNoteFromBpm(double bpm)
    {
        var ms = 600000 / bpm;
        return (int)ms;
    }

    private enum SendModes
    {
        Note_FixedDuration,
        Note_WhileTriggered,
    }

    private bool _triggered;

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
        
    [Input(Guid = "E1C1C77A-A6AF-4565-B345-F788BF268B2B")]
    public readonly InputSlot<bool> TriggerSend = new ();        
        
    [Input(Guid = "0E9E5C88-5FA4-40E4-8B02-CA2E9510057F", MappedType = typeof(SendModes))]
    public readonly InputSlot<int> SendMode = new ();

    [Input(Guid = "97A4F0D3-E691-4C2C-B731-7E4AFC77EED2")]
    public readonly InputSlot<string> Device = new ();
        
    [Input(Guid = "4B63108C-C5B7-42B9-ACDF-6BAC0E882D08")]
    public readonly InputSlot<int> ChannelNumber = new ();

    [Input(Guid = "90221C60-C7AC-4470-AFFB-E4FB22712EE5")]
    public readonly InputSlot<int> NoteNumber = new ();

    [Input(Guid = "A33619FA-BB33-4649-A714-285C5CCE15DC")]
    public readonly InputSlot<int> Velocity = new ();

    [Input(Guid = "B7ECA85F-3C35-43C3-9D79-2112D112C8C7")]
    public readonly InputSlot<bool> UseVelocityFloat = new();

    [Input(Guid = "5A99B601-328A-4238-9841-AB6022A932F5")]
    public readonly InputSlot<float> VelocityFloat = new ();

    [Input(Guid = "EDDB2482-F818-491B-A4E5-AF0E163DBA53")]
    public readonly InputSlot<float> DurationInSecs = new ();

    private NoteEvent _offEvent;
}