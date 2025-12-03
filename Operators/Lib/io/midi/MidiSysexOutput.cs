using NAudio.Midi;
using Operators.Utils;

namespace Lib.io.midi;

[Guid("78cadb24-5a77-41e2-908f-01d61140c769")]
internal sealed class MidiSysexOutput : Instance<MidiSysexOutput>
,MidiConnectionManager.IMidiConsumer,ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "b996a54f-0fe7-4eb3-8f29-66938eeb113e")]
    public readonly Slot<Command> Result = new();
    
    // We start with initialized true so it does not run when the project is loaded, only when triggered
    private bool _initialized = true;
    
    // Registered is true only if we have a midi device registered/connected
    private bool _registered;
    
    private List<byte> _bytes = new();

    public MidiSysexOutput()
    {
        Result.UpdateAction = Update;
    }

    
    protected override void Dispose(bool isDisposing)
    {
        if(!isDisposing) return;

        if (_initialized)
        {
            MidiConnectionManager.UnregisterConsumer(this);
            _registered = false;
        }
    }

    private void Update(EvaluationContext context)
    {
        var triggerActive = TriggerSend.GetValue(context);
        var deviceName = Device.GetValue(context);
        var foundDevice = false;

        if (triggerActive)
        {
            _initialized = !_initialized;
            
            // Clear the current registered midi device
            Dispose(true);
            // Clear the byte list
            _bytes.Clear();
        }

        if (!_initialized)
        {

            if (!_registered)
            {
                // Register the midi device
                MidiConnectionManager.RegisterConsumer(this);
                _registered = true;
            }

            var contextval = SysexString.GetValue(context);
            
            // Split the string by space character
            var characters = contextval.Split(' ');
                    
            // Iterate and build our byte list
            foreach (string word in characters)
            {
                try
                {
                    byte byteValue = byte.Parse(word, System.Globalization.NumberStyles.HexNumber);
                    _bytes.Add(byteValue);
                }                
                catch (Exception e)
                {
                    _lastErrorMessage = $"Failed to convert [{word}] to a byte: " + e.Message;
                    Log.Warning(_lastErrorMessage, this);
                    
                    // Set initialized so we do not loop
                    _initialized = true;
                    return;
                }
            }
            
            // If the starting byte of the sysex string does not equal midi exclusive start
            if (_bytes.ElementAt(0) != 240)
            {
                Log.Error($"Sysex String needs to start with midi exclusive start byte: F0", this);
                
                // Set initialized so we do not loop
                _initialized = true;
                return;
            }
                
            // If the end byte of the sysex string does not equal midi exclusive end
            if (_bytes.ElementAt(_bytes.Count-1) != 247)
            {
                Log.Error($"Sysex String needs to end with midi exclusive end byte: F7", this);
                
                // Set initialized so we do not loop
                _initialized = true;
                return;
            }
            

            foreach (var (m, device) in MidiConnectionManager.MidiOutsWithDevices)
            {
                if (device.ProductName != deviceName)
                    continue;

                try
                {

                    if (_bytes != null && triggerActive)
                    {
                        m.SendBuffer(_bytes.ToArray());
                        
                        Log.Debug($"Sent Sysex: [ " + contextval + " ] To: [ " + deviceName + " ]", this);
                    }

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

            _initialized = true;
        }
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
        
    [Input(Guid = "611e8c42-9954-421c-8071-1e959478c8fe")]
    public readonly InputSlot<bool> TriggerSend = new ();        

    [Input(Guid = "d7189d4a-b6c7-4d4f-9cee-1c37ed55b4e5")]
    public readonly InputSlot<string> Device = new ();
    
    [Input(Guid = "e054dece-d5ce-4675-9e24-d29e616ce7de")]
    public readonly InputSlot<string> SysexString = new();
        
}