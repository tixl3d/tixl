#nullable enable
using System.Diagnostics;
using System.Numerics;
using T3.Core.IO;
using T3.IoServices;

namespace Lib.io.serial;

[Guid("F830FAA6-02C1-475C-99AA-C5E0D9EA33B8")]
internal sealed class WLedSerialOutput : Instance<WLedSerialOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    public enum MapModes { Stretch, Repeat, Lerp }
    public enum ColorOrders { GRB, RGB, BRG, RBG, BGR, GBR }

    [Output(Guid = "CA36142C-CD17-41DF-B86C-CC2ED333AA61")] public readonly Slot<Command> Result = new();
    [Output(Guid = "23AE93A1-0C4A-4296-8DE6-B2B2CF187EF0")] public readonly Slot<bool> IsConnected = new();

    public WLedSerialOutput()
    {
        Result.UpdateAction = Update;
        IsConnected.UpdateAction = Update;
    }

    private string? _lastPortName;
    private int _lastBaudRate;
    private bool _lastConnectState;
    private byte[]? _frameBuffer;
    private int _bufferLedCount;
    private DateTime _nextRetryAt = DateTime.MinValue;

    private void Update(EvaluationContext context)
    {
        var sending = Sending.GetValue(context);
        var manualReconnect = Reconnect.GetValue(context);
        var portName = PortName.GetValue(context) ?? string.Empty;
        var baudRate = BaudRate.GetValue(context);
        var logMessages = LogMessages.GetValue(context);
        SerialConnectionManager.DebugLogSlowWrites = logMessages;

        var updateStartTicks = logMessages ? Stopwatch.GetTimestamp() : 0L;
        try
        {
            UpdateInner(context, sending, manualReconnect, portName, baudRate, logMessages);
        }
        finally
        {
            if (logMessages)
            {
                var totalMs = TicksToMilliseconds(Stopwatch.GetTimestamp() - updateStartTicks);
                if (totalMs > 0.5)
                    Log.Debug($"WLED Update total: {totalMs:F2}ms", this);
            }
        }
    }

    private void UpdateInner(EvaluationContext context, bool sending, bool manualReconnect, string portName, int baudRate, bool logMessages)
    {

        if (manualReconnect)
        {
            Reconnect.SetTypedInputValue(false);
            // Force the connection to be torn down (regardless of other subscribers,
            // hung writer thread, etc.) and reopened on the next frame.
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

        var colors = Colors.GetValue(context);
        if (colors == null || colors.Count == 0)
        {
            SetStatus("No colors - frame skipped", IStatusProvider.StatusLevel.Notice);
            return;
        }

        var requestedLedCount = LedCount.GetValue(context);
        var ledCount = requestedLedCount <= 0 ? colors.Count : requestedLedCount;
        if (ledCount > MaxLedCount)
            ledCount = MaxLedCount;

        var brightness = Brightness.GetValue(context);
        if (brightness < 0f)
            brightness = 0f;
        else if (brightness > 1f)
            brightness = 1f;

        var colorOrder = (ColorOrders)ColorOrder.GetValue(context);
        var mapMode = (MapModes)MapMode.GetValue(context);

        EnsureBuffer(ledCount);

        var loopStartTicks = logMessages ? Stopwatch.GetTimestamp() : 0L;

        // Position of R / G / B in the per-pixel triplet on the wire
        int rPos, gPos, bPos;
        switch (colorOrder)
        {
            case ColorOrders.RGB: rPos = 0; gPos = 1; bPos = 2; break;
            case ColorOrders.GRB: rPos = 1; gPos = 0; bPos = 2; break;
            case ColorOrders.BRG: rPos = 1; gPos = 2; bPos = 0; break;
            case ColorOrders.RBG: rPos = 0; gPos = 2; bPos = 1; break;
            case ColorOrders.BGR: rPos = 2; gPos = 1; bPos = 0; break;
            case ColorOrders.GBR: rPos = 2; gPos = 0; bPos = 1; break;
            default:              rPos = 1; gPos = 0; bPos = 2; break;
        }

        var srcCount = colors.Count;
        var buffer = _frameBuffer!;
        // Pre-compute the lerp scale only once (avoids divide in the hot loop)
        var lerpScale = (mapMode == MapModes.Lerp && ledCount > 1)
                            ? (srcCount - 1) / (float)(ledCount - 1)
                            : 0f;

        for (var i = 0; i < ledCount; i++)
        {
            float r, g, b;
            if (mapMode == MapModes.Repeat)
            {
                var c = colors[i % srcCount];
                r = c.X;
                g = c.Y;
                b = c.Z;
            }
            else if (mapMode == MapModes.Lerp && srcCount > 1 && ledCount > 1)
            {
                var t = i * lerpScale;
                var lo = (int)t;
                if (lo >= srcCount - 1)
                {
                    var c = colors[srcCount - 1];
                    r = c.X;
                    g = c.Y;
                    b = c.Z;
                }
                else
                {
                    var frac = t - lo;
                    var ca = colors[lo];
                    var cb = colors[lo + 1];
                    r = ca.X + (cb.X - ca.X) * frac;
                    g = ca.Y + (cb.Y - ca.Y) * frac;
                    b = ca.Z + (cb.Z - ca.Z) * frac;
                }
            }
            else
            {
                // Stretch (nearest-neighbor) and degenerate cases
                var srcIdx = (int)((long)i * srcCount / ledCount);
                if (srcIdx >= srcCount)
                    srcIdx = srcCount - 1;
                var c = colors[srcIdx];
                r = c.X;
                g = c.Y;
                b = c.Z;
            }

            var pixelStart = AdalightHeaderSize + i * 3;
            buffer[pixelStart + rPos] = ToByte(r * brightness);
            buffer[pixelStart + gPos] = ToByte(g * brightness);
            buffer[pixelStart + bPos] = ToByte(b * brightness);
        }

        long loopElapsedTicks = 0;
        if (logMessages)
        {
            loopElapsedTicks = Stopwatch.GetTimestamp() - loopStartTicks;
        }

        var writeStartTicks = logMessages ? Stopwatch.GetTimestamp() : 0L;
        var ok = SerialConnectionManager.WriteBytes(portName, buffer, buffer.Length);
        if (logMessages)
        {
            var writeElapsedTicks = Stopwatch.GetTimestamp() - writeStartTicks;
            var loopMs = TicksToMilliseconds(loopElapsedTicks);
            var writeMs = TicksToMilliseconds(writeElapsedTicks);
            if (writeMs > 0.5 || loopMs > 0.5)
            {
                Log.Debug($"WLED Update slow: loop={loopMs:F2}ms write={writeMs:F2}ms ({buffer.Length}B, {ledCount} LEDs)", this);
            }
        }

        if (!ok)
        {
            SetStatus($"Write to {portName} failed - connection lost", IStatusProvider.StatusLevel.Warning);
            IsConnected.Value = false;
            // No forced re-register here. If the writer thread truly died, it has closed
            // the port; IsPortOpen() will return false next frame and the throttled retry
            // path picks it up. Avoids a 100+ms main-thread teardown on transient hiccups.
        }
    }

    private static double TicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void EnsureBuffer(int ledCount)
    {
        var requiredSize = AdalightHeaderSize + ledCount * 3;
        if (_frameBuffer != null && _bufferLedCount == ledCount && _frameBuffer.Length == requiredSize)
            return;

        _frameBuffer = new byte[requiredSize];
        _bufferLedCount = ledCount;

        // Adalight header: 'A','d','a', countHi, countLo, checksum
        var countMinus1 = ledCount - 1;
        var hi = (byte)((countMinus1 >> 8) & 0xFF);
        var lo = (byte)(countMinus1 & 0xFF);
        _frameBuffer[0] = 0x41;
        _frameBuffer[1] = 0x64;
        _frameBuffer[2] = 0x61;
        _frameBuffer[3] = hi;
        _frameBuffer[4] = lo;
        _frameBuffer[5] = (byte)(hi ^ lo ^ 0x55);
    }

    private static byte ToByte(float v)
    {
        if (v <= 0f)
            return 0;
        if (v >= 1f)
            return 255;
        return (byte)(v * 255f + 0.5f);
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

    private const int AdalightHeaderSize = 6;
    private const int MaxLedCount = 4096;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    [Input(Guid = "166106D5-3354-440A-A37F-B21DDC49E597")] public readonly InputSlot<List<Vector4>> Colors = new();
    [Input(Guid = "2DF165C3-6441-444F-ABE1-2D1915794710")] public readonly InputSlot<int> LedCount = new(-1);
    [Input(Guid = "0128D1C3-0CFA-4ED1-8C8C-406B72A410BE", MappedType = typeof(MapModes))] public readonly InputSlot<int> MapMode = new((int)MapModes.Stretch);
    [Input(Guid = "6CFD3BAD-7C7B-45BC-9AB6-E7C5B30CB0B1", MappedType = typeof(ColorOrders))] public readonly InputSlot<int> ColorOrder = new((int)ColorOrders.GRB);
    [Input(Guid = "A9E27361-77AC-48FC-AE09-4BE0B2FF933B")] public readonly InputSlot<float> Brightness = new(1f);
    [Input(Guid = "8A58F4B4-3250-4344-AF5C-346929ED2E6F")] public readonly InputSlot<string> PortName = new();
    [Input(Guid = "BA4441FB-625E-4F49-8E9B-6C5237875F2B")] public readonly InputSlot<int> BaudRate = new(115200);
    [Input(Guid = "AFF14DDF-0A72-4EE3-BE79-EA44102DDD4F")] public readonly InputSlot<bool> Sending = new(true);
    [Input(Guid = "AD110199-D20A-48A8-8DC2-892E67E7A6AF")] public readonly InputSlot<bool> Reconnect = new();
    [Input(Guid = "72CFDEDC-7999-43C6-9615-BD4CAE0A8903")] public readonly InputSlot<bool> LogMessages = new();
}
