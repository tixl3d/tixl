#nullable enable
using System.Diagnostics;
using System.Threading;
using T3.Core.IO;
using T3.Core.Utils;

namespace Lib.io.dmx;

[Guid("7A610996-913A-443B-921A-12E6B05F4110")]
internal sealed class DmxOutput : Instance<DmxOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private readonly byte[] _dmxFrameBuffer = new byte[513];
    private readonly Stopwatch _stopwatch = new();

    [Input(Guid = "0E2F7F23-99A1-428A-9343-263B29831BC3")]
    public readonly InputSlot<bool> Connect = new();

    [Input(Guid = "59381A83-3736-4767-948A-18A81180630C")]
    public readonly InputSlot<List<int>> DmxUniverse = new();

    [Output(Guid = "A0D0B722-1959-4240-812A-1845C3A5A096")]
    public readonly Slot<bool> IsConnected = new();

    [Input(Guid = "C1F747F5-3634-4142-A16D-346743A13728")]
    public readonly InputSlot<int> MaxFps = new(40);

    [Input(Guid = "2845601E-2374-4581-8843-169289290176")]
    public readonly InputSlot<string> PortName = new();

    [Output(Guid = "D2C7B38A-8514-4521-884F-66D605485458")]
    public readonly Slot<Command> Result = new();

    private bool _lastConnectState;
    private string? _lastErrorMessage = "Disconnected";

    private string? _lastPortName;
    private long _nextFrameTimeTicks;
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;

    public DmxOutput()
    {
        Result.UpdateAction = Update;
        IsConnected.UpdateAction = Update;
    }

    string ICustomDropdownHolder.GetValueForInput(Guid id)
    {
        if (PortName.Value == null)
            return string.Empty;
        
        return id == PortName.Id 
                   ? PortName.Value 
                   : string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
    {
        return id == PortName.Id ? SerialConnectionManager.GetAvailableSerialPortsWithDescriptions() : Empty<string>();
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (string.IsNullOrEmpty(s) || !i || id != PortName.Id) return;
        PortName.SetTypedInputValue(SerialConnectionManager.GetPortNameFromDeviceDescription(s));
    }

    public void Dispose()
    {
        SerialConnectionManager.Unregister(this, _lastPortName);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return _statusLevel;
    }

    public string? GetStatusMessage()
    {
        return _lastErrorMessage;
    }

    private void Update(EvaluationContext context)
    {
        var shouldConnect = Connect.GetValue(context);
        var portName = PortName.GetValue(context) ?? string.Empty;

        var settingsChanged = portName != _lastPortName || shouldConnect != _lastConnectState;
        if (settingsChanged)
        {
            SerialConnectionManager.Unregister(this, _lastPortName);

            if (shouldConnect && !string.IsNullOrEmpty(portName))
                try
                {
                    SerialConnectionManager.Register(this, portName, 250000, null, PortModes.DMX);
                    SetStatus($"Connected to {portName}", IStatusProvider.StatusLevel.Success);
                }
                catch (Exception e)
                {
                    SetStatus($"Failed to connect: {e.Message}", IStatusProvider.StatusLevel.Error);
                }
            else
                SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);

            _lastPortName = portName;
            _lastConnectState = shouldConnect;
        }

        var isConnected = SerialConnectionManager.IsPortOpen(portName);
        IsConnected.Value = isConnected;
        if (!isConnected) return;

        var maxFps = MaxFps.GetValue(context);
        if (maxFps > 0)
        {
            if (!_stopwatch.IsRunning) _stopwatch.Start();
            while (_stopwatch.ElapsedTicks < _nextFrameTimeTicks)
                if (_nextFrameTimeTicks - _stopwatch.ElapsedTicks > Stopwatch.Frequency / 1000) Thread.Sleep(1);
                else Thread.SpinWait(100);

            _nextFrameTimeTicks += Stopwatch.Frequency / maxFps;
        }

        if (DmxUniverse.DirtyFlag.IsDirty)
        {
            var universeData = DmxUniverse.GetValue(context);
            Array.Clear(_dmxFrameBuffer, 1, 512);
            if (universeData != null)
            {
                var count = Math.Min(universeData.Count, 512);
                for (var i = 0; i < count; i++)
                    _dmxFrameBuffer[i + 1] = (byte)universeData[i].Clamp(0, 255);
            }
        }

        SerialConnectionManager.SendDmxFrame(portName, _dmxFrameBuffer);
    }

    public void SetStatus(string m, IStatusProvider.StatusLevel l)
    {
        _lastErrorMessage = m;
        _statusLevel = l;
    }
}