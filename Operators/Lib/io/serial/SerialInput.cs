#nullable enable
using T3.Core.IO;
using T3.Core.Utils;

namespace Lib.io.serial;

[Guid("F5AABC51-B275-4344-A244-934F4E4F7D3A")]
internal sealed class SerialInput : Instance<SerialInput>, IStatusProvider, ICustomDropdownHolder, ISerialReceiver, IDisposable
{
    [Output(Guid = "232E4166-433E-497B-85A5-D275A0D4B272", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> ReceivedString = new();
    [Output(Guid = "0E25FE8E-6C14-4E6E-824E-910B8D9E3D8E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<string>> ReceivedLines = new();
    [Output(Guid = "E3B935A1-58A2-4467-B943-2559779E1430", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasTrigger = new();
    [Output(Guid = "7C887943-4C73-4402-8636-E650D4507563")]
    public readonly Slot<bool> IsConnected = new();

    public SerialInput()
    {
        ReceivedString.UpdateAction = Update;
        ReceivedLines.UpdateAction = Update;
        WasTrigger.UpdateAction = Update;
        IsConnected.UpdateAction = Update;
    }

    private string? _lastPortName;
    private int _lastBaudRate;
    private bool _lastConnectState;

    private void Update(EvaluationContext context)
    {
        var listLength = ListLength.GetValue(context).Clamp(1, 1000);
        var wasTriggered = false;
        while (_receivedQueue.TryDequeue(out var line))
        {
            ReceivedString.Value = line;
            _messageHistory.Add(line);
            wasTriggered = true;
        }
        while (_messageHistory.Count > listLength) { _messageHistory.RemoveAt(0); }
        ReceivedLines.Value = _messageHistory;
        WasTrigger.Value = wasTriggered;

        var shouldConnect = Connect.GetValue(context);
        var portName = PortName.GetValue(context);
        var baudRate = BaudRate.GetValue(context);

        var settingsChanged = portName != _lastPortName || baudRate != _lastBaudRate || shouldConnect != _lastConnectState;
        if (settingsChanged)
        {
            SerialConnectionManager.Unregister(this, _lastPortName);
            if (shouldConnect)
            {
                try { SerialConnectionManager.Register(this, portName, baudRate, this, PortModes.Standard); }
                catch (Exception e) { SetStatus($"Failed to connect: {e.Message}", IStatusProvider.StatusLevel.Error); }
            }
            else { SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice); }
            _lastPortName = portName;
            _lastBaudRate = baudRate;
            _lastConnectState = shouldConnect;
        }
        IsConnected.Value = SerialConnectionManager.IsPortOpen(portName);
    }

    public void Dispose() { SerialConnectionManager.Unregister(this, PortName.Value); }
    void ISerialReceiver.ReceiveLine(string line) { _receivedQueue.Enqueue(line); ReceivedString.DirtyFlag.Invalidate(); }

    private readonly ConcurrentQueue<string> _receivedQueue = new();
    private readonly List<string> _messageHistory = new();
    private string? _lastErrorMessage = "Disconnected";
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _statusLevel = l; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string? GetStatusMessage() => _lastErrorMessage;
    string ICustomDropdownHolder.GetValueForInput(Guid id) => id == PortName.Id ? PortName.Value : string.Empty;
    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id) => id == PortName.Id ? SerialConnectionManager.GetAvailableSerialPortsWithDescriptions() : Enumerable.Empty<string>();
    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (string.IsNullOrEmpty(s) || !i || id != PortName.Id) return;
        PortName.SetTypedInputValue(SerialConnectionManager.GetPortNameFromDeviceDescription(s));
    }

    [Input(Guid = "D21E4E71-6731-4A2D-B32E-30810D3C185A")] public readonly InputSlot<string> PortName = new();
    [Input(Guid = "8051E8E3-4050-4284-A6A4-2E11C4731804")] public readonly InputSlot<int> BaudRate = new(9600);
    [Input(Guid = "4B83B1B5-391B-4A8E-8734-706B1B85169F")] public readonly InputSlot<int> ListLength = new(10);
    [Input(Guid = "235C441E-9214-4E3F-84E3-441F8C43098C")] public readonly InputSlot<bool> Connect = new();
}