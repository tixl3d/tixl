using NAudio.Midi;
using T3.IoServices;
using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("61c11adb-94f0-4dc7-9611-c22c40709cf4")]
internal sealed class MidiTriggerOutput : Instance<MidiTriggerOutput>, MidiConnectionManager.IMidiConsumer, ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "5E07FF38-A990-4BB7-B166-1A647F8C7933")]
    public readonly Slot<Command> Result = new();

    public MidiTriggerOutput()
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
        var channel = ChannelNumber.GetValue(context).Clamp(1, 16);
        var trigPC = TriggerProgramChange.GetValue(context);
        var programChange = ProgramChangeNumber.GetValue(context).Clamp(0, 127);
        var trigStart = TriggerStart.GetValue(context);
        var trigStop = TriggerStop.GetValue(context);
        var trigContinue = TriggerContinue.GetValue(context);
        var trigTempo = TriggerTempoEvent.GetValue(context);

        var activePC = false;
        var activeStart = false;
        var activeStop = false;
        var activeContinue = false;
        var activeTempo = false;


        if (!_initialized)
        {
            MidiConnectionManager.RegisterConsumer(this);
            _initialized = true;
        }

        //Only trigger once:

        if (trigPC != _triggeredPC)
        {
            if (trigPC)
            {
                activePC = true;
            }
            _triggeredPC = trigPC;
        }

        if (trigStart != _triggeredStart)
        {
            if (trigStart)
            {
                activeStart = true;
            }
            _triggeredStart = trigStart;
        }

        if (trigStop != _triggeredStop)
        {
            if (trigStop)
            {
                activeStop = true;
            }
            _triggeredStop = trigStop;
        }

        if (trigContinue != _triggeredContinue)
        {
            if (trigContinue)
            {
                activeContinue = true;
            }
            _triggeredContinue = trigContinue;
        }

        if (trigTempo != _triggeredTempo)
        {
            if (trigTempo)
            {
                activeTempo = true;
            }
            _triggeredTempo = trigTempo;
        }


        var absTime = (long)Playback.RunTimeInSecs * 1000;


        foreach (var (m, device) in MidiConnectionManager.MidiOutsWithDevices)
        {
            if (device.ProductName != deviceName)
                continue;           
                
            try
            {
                // In theory all can be triggered at once, therefore no case or if/else
                if(activePC)
                {
                    m.Send(new PatchChangeEvent(0, channel, programChange).GetAsShortMessage());
                }
                if (activeStart)
                {
                    m.Send(new MidiEvent(0, channel, MidiCommandCode.StartSequence).GetAsShortMessage());

                }
                if (activeStop)
                {
                    m.Send(new MidiEvent(0, channel, MidiCommandCode.StopSequence).GetAsShortMessage());

                }
                if (activeContinue)
                {
                    m.Send(new MidiEvent(0, channel, MidiCommandCode.ContinueSequence).GetAsShortMessage());

                }
                if (activeTempo)
                {
                    m.Send(new TempoEvent(GetMicrosecondsPerQuarterNoteFromBpm(Playback.Current.Bpm), 0).GetAsShortMessage());

                }

                foundDevice = true;
            }
            catch (Exception e)
            {
                _lastErrorMessage = $"Failed to send midi to {deviceName}: " + e.Message;
                Log.Warning(_lastErrorMessage, this);
            }
                
        }
        _lastErrorMessage = !foundDevice ? $"Can't find MidiDevice {deviceName}" : null;
    }
        
    private static int GetMicrosecondsPerQuarterNoteFromBpm(double bpm)
    {
        var ms = 600000 / bpm;
        return (int)ms;
    }

    private bool _triggeredPC;
    private bool _triggeredStart;
    private bool _triggeredStop;
    private bool _triggeredContinue;
    private bool _triggeredTempo;

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
        
    [Input(Guid = "C134F57A-A40C-42E4-8223-5CD695B15CAA")]
    public readonly InputSlot<string> Device = new ();
        
    [Input(Guid = "4B63108C-C5B7-42B9-ACDF-6BAC0E882D08")]
    public readonly InputSlot<int> ChannelNumber = new ();

    [Input(Guid = "5626D449-EF28-4D68-8805-014454F74F6B")]
    public readonly InputSlot<bool> TriggerProgramChange = new ();

    [Input(Guid = "90221C60-C7AC-4470-AFFB-E4FB22712EE5")]
    public readonly InputSlot<int> ProgramChangeNumber = new ();

    [Input(Guid = "8F5E7919-09AE-4D38-A404-93AD6D9EA6ED")]
    public readonly InputSlot<bool> TriggerStart = new();

    [Input(Guid = "09BB029E-5A17-4986-B2CB-94668C14C814")]
    public readonly InputSlot<bool> TriggerStop = new();

    [Input(Guid = "9A4DB45A-06DA-4C47-BFFF-860AAE8D9DFE")]
    public readonly InputSlot<bool> TriggerContinue = new();

    [Input(Guid = "7652082C-70B2-4FFE-88AA-63B9C323836F")]
    public readonly InputSlot<bool> TriggerTempoEvent = new();
}