using NAudio.Midi;
using T3.IoServices;
using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("f9f4281b-92ee-430d-a930-6b588a5cb9a9")]
internal sealed class MidiOutput : Instance<MidiOutput>, MidiConnectionManager.IMidiConsumer, ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "670C784C-DE53-46F4-B93A-A1F07AA8F18E")]
    public readonly Slot<Command> Result = new();

    public MidiOutput()
    {
        Result.UpdateAction += Update;
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
        var noteOrControllerIndex = NoteOrController.GetValue(context).Clamp(0, 127);
            
        var velocity = (int)(Velocity.GetValue(context).Clamp(0, 1) * 127);
        var intVelocity = Velocity127.GetValue(context);
        if (intVelocity >= 0)
        {
            velocity = intVelocity.Clamp(0, 127);
        }
        var durationInMs = ((int)(DurationInSecs.GetValue(context)*1000)).Clamp(1, 100000);
        var sendMode = SendMode.GetEnumValue<SendModes>(context);
        var triggerActive = TriggerSend.GetValue(context);

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
            
            
        foreach (var (m, device) in MidiConnectionManager.MidiOutsWithDevices)
        {
            if (device.ProductName != deviceName)
                continue;
                
            var channel = ChannelNumber.GetValue(context).Clamp(1,16);
            try
            {
                MidiEvent midiEvent =null;
                switch (sendMode)
                {
                    case SendModes.Note_WhileTriggered:
                        if (triggerJustActivated)
                        {
                            midiEvent = new NoteOnEvent(0, channel, noteOrControllerIndex, velocity, durationInMs);
                        }
                        else if (triggerJustDeactivated)
                        {
                            midiEvent = new NoteOnEvent(0, channel, noteOrControllerIndex, 0, durationInMs);
                        }
                        break;
                        
                    case SendModes.Notes_FixedDuration:
                        if (triggerActive)
                        {
                            var noteOnEvent= new NoteOnEvent(0, channel, noteOrControllerIndex, velocity, durationInMs);
                            midiEvent = noteOnEvent;
                            _lastNoteOnTime = Playback.RunTimeInSecs;
                            _offEvent = noteOnEvent.OffEvent;
                        }
                        else if (Playback.RunTimeInSecs - _lastNoteOnTime > durationInMs / 1000.0)
                        {
                            midiEvent = _offEvent;
                            _offEvent = null;
                        }
                        break;
                        
                    case SendModes.ControllerChange:
                        midiEvent = new ControlChangeEvent(0, channel, (MidiController)noteOrControllerIndex, velocity);
                        break;
                        
                    case SendModes.StartSequence:
                        midiEvent = new MidiEvent(0, channel, MidiCommandCode.StartSequence);
                        break;
                        
                    case SendModes.StopSequence:
                        midiEvent = new MidiEvent(0, channel, MidiCommandCode.StopSequence);
                        break;
                        
                    case SendModes.ContinueSequence:
                        midiEvent = new MidiEvent(0, channel, MidiCommandCode.ContinueSequence);
                        break;

                    case SendModes.TempoEvent:
                        midiEvent = new TempoEvent(GetMicrosecondsPerQuarterNoteFromBpm(Playback.Current.Bpm),0);
                        break;

                }
                if(midiEvent != null)
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
        
    private double _lastNoteOnTime;

    private static int GetMicrosecondsPerQuarterNoteFromBpm(double bpm)
    {
        var ms = 600000 / bpm;
        return (int)ms;
    }

    private enum SendModes
    {
        Notes_FixedDuration,
        Note_WhileTriggered,
        ControllerChange,
            
        StartSequence,
        StopSequence,
        ContinueSequence,
        TempoEvent
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
        
    [Input(Guid = "9EFA1BE1-F102-4457-A69B-2AEF59F6B845")]
    public readonly InputSlot<bool> TriggerSend = new ();
        
        
    [Input(Guid = "EC4D83B6-78EB-4CAC-826B-CAFB0BE3F604", MappedType = typeof(SendModes))]
    public readonly InputSlot<int> SendMode = new ();

    [Input(Guid = "ADEA6968-35EF-436A-BC2D-D9433B623DF6")]
    public readonly InputSlot<string> Device = new ();
        
    [Input(Guid = "A7E1EAC2-5602-4C40-8519-19CA53763C76")]
    public readonly InputSlot<int> ChannelNumber = new ();

    [Input(Guid = "0FFF2CE2-DEFA-442C-A089-4B12E7D71620")]
    public readonly InputSlot<int> NoteOrController = new ();

    [Input(Guid = "61CCD308-0006-42DE-B190-C006E99B5871")]
    public readonly InputSlot<float> Velocity = new ();
        
    [Input(Guid = "A10E3F7B-D132-49CA-9F6D-726D5E699443")]
    public readonly InputSlot<int> Velocity127 = new (-1);
        
    [Input(Guid = "ABE9393E-282E-4DE0-8F86-541FA955658F")]
    public readonly InputSlot<float> DurationInSecs = new ();

    private NoteEvent _offEvent;
}