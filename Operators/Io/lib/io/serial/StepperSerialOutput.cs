#nullable enable
using T3.Core.IO;
using T3.IoServices;

namespace Lib.io.serial;

[Guid("7f3a2c10-5b84-4e29-9a6d-1c7e0b45d832")]
internal sealed class StepperSerialOutput : Instance<StepperSerialOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    [Output(Guid = "9c2e1b44-6a31-4d58-b7e0-2f5a8c91d3e6")] public readonly Slot<Command> Result = new();
    [Output(Guid = "3d8f7a21-9b04-4c6e-8a15-7e2d9f0b6c43")] public readonly Slot<bool> IsConnected = new();

    public StepperSerialOutput()
    {
        Result.UpdateAction = Update;
        IsConnected.UpdateAction = Update;
    }

    private string? _lastPortName;
    private int _lastBaudRate;
    private bool _lastConnectState;
    private DateTime _nextRetryAt = DateTime.MinValue;
    private readonly byte[] _packet = new byte[10];

    private void Update(EvaluationContext context)
    {
        var sending = Sending.GetValue(context);
        var manualReconnect = Reconnect.GetValue(context);
        var portName = PortName.GetValue(context) ?? string.Empty;
        var baudRate = BaudRate.GetValue(context);

        if (manualReconnect)
        {
            Reconnect.SetTypedInputValue(false);
            SerialConnectionManager.ForceClose(_lastPortName);
            SerialConnectionManager.ForceClose(portName);
            _lastConnectState = false;
            _nextRetryAt = DateTime.MinValue;
        }

        var settingsChanged = portName != _lastPortName || baudRate != _lastBaudRate || sending != _lastConnectState;
        if (settingsChanged)
        {
            SerialConnectionManager.Unregister(this, _lastPortName);
            if (sending && !string.IsNullOrEmpty(portName))
            {
                try
                {
                    SerialConnectionManager.Register(this, portName, baudRate, null, PortModes.Standard);
                    SetStatus($"Connected to {portName} @ {baudRate}", IStatusProvider.StatusLevel.Success);
                    _nextRetryAt = DateTime.MinValue;
                }
                catch (Exception e)
                {
                    SetStatus($"Failed to connect: {e.Message}", IStatusProvider.StatusLevel.Warning);
                }
            }
            else
            {
                SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
            }
            _lastPortName = portName;
            _lastBaudRate = baudRate;
            _lastConnectState = sending;
        }

        var isConnected = SerialConnectionManager.IsPortOpen(portName);

        // Throttled auto-reconnect when the port has dropped
        if (sending && !isConnected && !string.IsNullOrEmpty(portName))
        {
            var now = DateTime.UtcNow;
            if (now >= _nextRetryAt)
            {
                _nextRetryAt = now + RetryInterval;
                try
                {
                    SerialConnectionManager.Register(this, portName, baudRate, null, PortModes.Standard);
                    SetStatus($"Reconnected to {portName}", IStatusProvider.StatusLevel.Success);
                    isConnected = true;
                }
                catch (Exception e)
                {
                    SetStatus($"Reconnecting to {portName}... ({e.Message})", IStatusProvider.StatusLevel.Warning);
                }
            }
        }

        IsConnected.Value = isConnected;
        if (!isConnected)
            return;

        var stepsPerRev = StepsPerRevolution.GetValue(context);
        if (stepsPerRev <= 0)
            stepsPerRev = 4096;

        var maxSpeed = MaxSpeed.GetValue(context);
        if (maxSpeed < 0f)
            maxSpeed = 0f;

        var s0 = DegPerSecToSteps(SpeedX.GetValue(context), stepsPerRev, maxSpeed);
        var s1 = DegPerSecToSteps(SpeedY.GetValue(context), stepsPerRev, maxSpeed);
        var s2 = DegPerSecToSteps(SpeedZ.GetValue(context), stepsPerRev, maxSpeed);

        // "Stp" + 3x int16 little-endian velocity (steps/s) + XOR checksum
        var p = _packet;
        p[0] = (byte)'S';
        p[1] = (byte)'t';
        p[2] = (byte)'p';
        p[3] = (byte)(s0 & 0xFF);
        p[4] = (byte)((s0 >> 8) & 0xFF);
        p[5] = (byte)(s1 & 0xFF);
        p[6] = (byte)((s1 >> 8) & 0xFF);
        p[7] = (byte)(s2 & 0xFF);
        p[8] = (byte)((s2 >> 8) & 0xFF);
        p[9] = (byte)(p[3] ^ p[4] ^ p[5] ^ p[6] ^ p[7] ^ p[8] ^ 0x55);

        var ok = SerialConnectionManager.WriteBytes(portName, p, p.Length);
        if (!ok)
        {
            SetStatus($"Write to {portName} failed - connection lost", IStatusProvider.StatusLevel.Warning);
            IsConnected.Value = false;
        }
    }

    private static short DegPerSecToSteps(float degPerSec, int stepsPerRev, float maxDegPerSec)
    {
        if (maxDegPerSec > 0f)
        {
            if (degPerSec > maxDegPerSec)
            {
                degPerSec = maxDegPerSec;
            }
            else if (degPerSec < -maxDegPerSec)
            {
                degPerSec = -maxDegPerSec;
            }
        }

        var steps = degPerSec / 360f * stepsPerRev;
        if (steps > 32767f)
        {
            steps = 32767f;
        }
        else if (steps < -32767f)
        {
            steps = -32767f;
        }

        return (short)(steps >= 0f ? steps + 0.5f : steps - 0.5f);
    }

    public void Dispose() { SerialConnectionManager.Unregister(this, PortName.Value); }

    private string? _statusMessage;
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _statusMessage = m; _statusLevel = l; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string? GetStatusMessage() => _statusMessage;

    string ICustomDropdownHolder.GetValueForInput(Guid id) => id == PortName.Id ? PortName.Value ?? string.Empty : string.Empty;
    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id) => id == PortName.Id ? SerialConnectionManager.GetAvailableSerialPortsWithDescriptions() : Enumerable.Empty<string>();
    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (string.IsNullOrEmpty(s) || !i || id != PortName.Id)
            return;
        PortName.SetTypedInputValue(SerialConnectionManager.GetPortNameFromDeviceDescription(s));
    }

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    [Input(Guid = "2c4e6a80-0001-4f10-9c30-aa0000000001")] public readonly InputSlot<float> SpeedX = new(0f);
    [Input(Guid = "2c4e6a80-0002-4f10-9c30-aa0000000002")] public readonly InputSlot<float> SpeedY = new(0f);
    [Input(Guid = "2c4e6a80-0003-4f10-9c30-aa0000000003")] public readonly InputSlot<float> SpeedZ = new(0f);
    [Input(Guid = "2c4e6a80-0004-4f10-9c30-aa0000000004")] public readonly InputSlot<int> StepsPerRevolution = new(4096);
    [Input(Guid = "2c4e6a80-0005-4f10-9c30-aa0000000005")] public readonly InputSlot<float> MaxSpeed = new(60f);
    [Input(Guid = "2c4e6a80-0006-4f10-9c30-aa0000000006")] public readonly InputSlot<string> PortName = new();
    [Input(Guid = "2c4e6a80-0007-4f10-9c30-aa0000000007")] public readonly InputSlot<int> BaudRate = new(115200);
    [Input(Guid = "2c4e6a80-0008-4f10-9c30-aa0000000008")] public readonly InputSlot<bool> Sending = new(true);
    [Input(Guid = "2c4e6a80-0009-4f10-9c30-aa0000000009")] public readonly InputSlot<bool> Reconnect = new();
}
