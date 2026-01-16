#nullable enable
using T3.Core.IO;

namespace Lib.io.serial;

[Guid("B469D554-A69F-44A0-A03F-331853B82199")]
internal sealed class SerialOutput : Instance<SerialOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    [Output(Guid = "26C9848A-7067-4592-AE8D-26496F435017")] public readonly Slot<Command> Result = new();
    [Output(Guid = "1B91E3A7-92F6-44A2-B086-A82FDEE9426D")] public readonly Slot<bool> IsConnected = new();

    public SerialOutput() { Result.UpdateAction = Update; IsConnected.UpdateAction = Update; }

    private string? _lastPortName;
    private int _lastBaudRate;
    private bool _lastConnectState;
    private string? _lastSentMessage;

    private void Update(EvaluationContext context)
    {
        var shouldConnect = Connect.GetValue(context);
        var portName = PortName.GetValue(context) ?? string.Empty;
        var baudRate = BaudRate.GetValue(context);

        var settingsChanged = portName != _lastPortName || baudRate != _lastBaudRate || shouldConnect != _lastConnectState;
        if (settingsChanged)
        {
            SerialConnectionManager.Unregister(this, _lastPortName);
            if (shouldConnect)
            {
                try
                {
                    SerialConnectionManager.Register(this, portName, baudRate, null, PortModes.Standard);
                    SetStatus($"Connected to {portName}", IStatusProvider.StatusLevel.Success);
                }
                catch (Exception e) { SetStatus($"Failed to connect: {e.Message}", IStatusProvider.StatusLevel.Error); }
            }
            else { SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice); }
            _lastPortName = portName;
            _lastBaudRate = baudRate;
            _lastConnectState = shouldConnect;
        }

        var isConnected = SerialConnectionManager.IsPortOpen(portName);
        IsConnected.Value = isConnected;

        var sendOnChange = SendOnChange.GetValue(context);
        var separator = Separator.GetValue(context) ?? "";
        var messageParts = MessageParts.GetCollectedTypedInputs().Select(p => p.GetValue(context));
        var currentMessage = string.Join(separator, messageParts);
        var hasMessageChanged = currentMessage != _lastSentMessage;
        var manualTrigger = SendTrigger.GetValue(context);
        var shouldSend = manualTrigger || (sendOnChange && hasMessageChanged);

        if (isConnected && shouldSend)
        {
            if (manualTrigger) SendTrigger.SetTypedInputValue(false);
            if (!string.IsNullOrEmpty(currentMessage) && !string.IsNullOrEmpty(portName))
            {
                if (AddLineEnding.GetValue(context))
                {
                    SerialConnectionManager.WriteLine(portName, currentMessage);
                }
                else
                {
                    SerialConnectionManager.Write(portName, currentMessage);
                }
                _lastSentMessage = currentMessage;
            }
        }
    }

    public void Dispose() { SerialConnectionManager.Unregister(this, PortName.Value); }

    private string? _lastErrorMessage;
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _statusLevel = l; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string? GetStatusMessage() => _lastErrorMessage;
    string ICustomDropdownHolder.GetValueForInput(Guid id) => id == PortName.Id ? PortName.Value ?? string.Empty : string.Empty;
    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id) => id == PortName.Id ? SerialConnectionManager.GetAvailableSerialPortsWithDescriptions() : Enumerable.Empty<string>();
    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (string.IsNullOrEmpty(s) || !i || id != PortName.Id) return;
        PortName.SetTypedInputValue(SerialConnectionManager.GetPortNameFromDeviceDescription(s));
    }

    [Input(Guid = "3F9E404B-993A-427E-8367-9A9772F76426")] public readonly InputSlot<string> PortName = new();
    [Input(Guid = "262D4A18-354A-4835-9B22-458DB18579E7")] public readonly InputSlot<int> BaudRate = new(9600);
    [Input(Guid = "AA199834-537D-4C67-9ECB-88358252D263")] public readonly MultiInputSlot<string> MessageParts = new();
    [Input(Guid = "B9C6837C-4AD3-424C-976E-1E4552A85523")] public readonly InputSlot<string> Separator = new(" ");
    [Input(Guid = "C796590A-335B-4C21-8898-356D3B69601F")] public readonly InputSlot<bool> SendTrigger = new();
    [Input(Guid = "A0E35517-337A-4770-985A-34CAC95B5B5F")] public readonly InputSlot<bool> SendOnChange = new(true);
    [Input(Guid = "DAF8E832-6D55-4424-AEF9-A92543B1A796")] public readonly InputSlot<bool> AddLineEnding = new(true);
    [Input(Guid = "7414A929-8A33-405D-A466-933E31972B57")] public readonly InputSlot<bool> Connect = new();
}