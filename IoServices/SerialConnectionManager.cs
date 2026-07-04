#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;

namespace T3.IoServices;

public enum PortModes { Standard, DMX }

public interface ISerialReceiver
{
    void ReceiveLine(string line);
    void SetStatus(string message, IStatusProvider.StatusLevel level);
}

public static class SerialConnectionManager
{
    /// <summary>
    /// When true, the writer thread logs serial-write durations above 0.5 ms.
    /// Driven from operator-side LogMessages toggles. Cheap volatile read on the hot path.
    /// </summary>
    public static volatile bool DebugLogSlowWrites;

    private static readonly Dictionary<string, PortConnection> _connections = new();
    private static readonly object _lock = new();

    #region Connection Management
    public static void Register(object owner, string portName, int baudRate, ISerialReceiver? receiver, PortModes mode)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(portName)) return;

            // If a previous PortConnection here died (writer thread exited on a USB hiccup,
            // device unplugged, etc.), dispose it and create a fresh one. Otherwise we'd
            // keep handing out subscriptions to a zombie connection.
            if (_connections.TryGetValue(portName, out var existing) && !existing.IsAlive)
            {
                existing.Dispose();
                _connections.Remove(portName);
            }

            if (!_connections.TryGetValue(portName, out var connection))
            {
                connection = new PortConnection(portName, baudRate, mode);
                _connections[portName] = connection;
            }

            if (connection.Mode != mode)
            {
                throw new InvalidOperationException($"Cannot register port {portName} for {mode} mode. It is already open in {connection.Mode} mode.");
            }

            connection.AddSubscriber(owner, receiver);
        }
    }

    /// <summary>
    /// Forcibly disposes and removes a PortConnection regardless of subscriber count.
    /// Use when a connection is wedged (hung writer thread, zombie state) and a clean
    /// teardown is needed before reconnecting. Subscribers should re-Register afterward.
    /// </summary>
    public static void ForceClose(string? portName)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(portName) || !_connections.TryGetValue(portName, out var connection)) return;
            connection.Dispose();
            _connections.Remove(portName);
        }
    }

    public static void Unregister(object owner, string? portName)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(portName) || !_connections.TryGetValue(portName, out var connection)) return;
            connection.RemoveSubscriber(owner);
            if (connection.SubscriberCount == 0)
            {
                connection.Dispose();
                _connections.Remove(portName);
            }
        }
    }

    public static bool IsPortOpen(string? portName)
    {
        lock (_lock) { return !string.IsNullOrEmpty(portName) && _connections.TryGetValue(portName, out var c) && c.IsOpen; }
    }
    #endregion

    #region Data Methods
    public static void WriteLine(string portName, string data) { lock (_lock) { if (_connections.TryGetValue(portName, out var c)) c.WriteLine(data); } }
    public static void Write(string portName, string data) { lock (_lock) { if (_connections.TryGetValue(portName, out var c)) c.Write(data); } }
    public static void SendDmxFrame(string portName, byte[] dmxFrame) { lock (_lock) { if (_connections.TryGetValue(portName, out var c)) c.SendDmxFrame(dmxFrame); } }
    public static bool WriteBytes(string portName, byte[] data, int length) { lock (_lock) { return _connections.TryGetValue(portName, out var c) && c.WriteBytes(data, length); } }
    #endregion

    #region Port Scanning
    private static List<string>? _cachedPortList;
    private static readonly Stopwatch _portListCacheStopwatch = new();
    private static int _portListRefreshing;
    private const int CacheDurationMs = 2000;
    private static readonly Regex _comPortRegex = new(@"\((COM\d+)\)", RegexOptions.Compiled);

    // Returns the cached port list immediately. When the cache is stale, kicks off a
    // background refresh so the UI thread never blocks on the ~200-400 ms WMI query.
    // Only the very first call (no cache yet) does a synchronous query to populate.
    public static List<string> GetAvailableSerialPortsWithDescriptions()
    {
        var cacheStale = !_portListCacheStopwatch.IsRunning || _portListCacheStopwatch.ElapsedMilliseconds >= CacheDurationMs;

        if (_cachedPortList == null)
        {
            // Cold start: populate synchronously so the dropdown isn't empty.
            _cachedPortList = QueryPortListBlocking();
            _portListCacheStopwatch.Restart();
            return _cachedPortList;
        }

        if (cacheStale && Interlocked.CompareExchange(ref _portListRefreshing, 1, 0) == 0)
        {
            ThreadPool.QueueUserWorkItem(static _ =>
            {
                try
                {
                    var fresh = QueryPortListBlocking();
                    _cachedPortList = fresh;
                    _portListCacheStopwatch.Restart();
                }
                finally
                {
                    Volatile.Write(ref _portListRefreshing, 0);
                }
            });
        }

        return _cachedPortList;
    }

    private static List<string> QueryPortListBlocking()
    {
        var portList = new List<string>();
        try
        {
            var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)'");
            portList.AddRange(searcher.Get().Cast<ManagementObject>().Select(p => p["Name"]?.ToString()).Where(s => s != null)!);
        }
        catch (Exception ex)
        {
            Log.Warning($"WMI query for serial ports failed, falling back to basic list. Error: {ex.Message}");
            return SerialPort.GetPortNames().ToList();
        }
        return portList.OrderBy(s => s).ToList();
    }

    public static string GetPortNameFromDeviceDescription(string description)
    {
        var match = _comPortRegex.Match(description);
        return match.Success ? match.Groups[1].Value : description;
    }
    #endregion

    private class PortConnection : IDisposable
    {
        public bool IsOpen => _serialPort.IsOpen;
        public bool IsAlive => _keepRunning && _serialPort.IsOpen;
        public int SubscriberCount => _subscribers.Count;
        public readonly PortModes Mode;

        private readonly SerialPort _serialPort;
        private Thread? _workerThread;
        private readonly Thread? _writerThread;
        private volatile bool _keepRunning;
        private readonly HashSet<object> _subscribers = new();
        private readonly List<ISerialReceiver> _receivers = new();

        private readonly object _frameLock = new();
        private readonly AutoResetEvent _frameReady = new(false);
        private byte[]? _pendingFrame;
        private int _pendingFrameLength;
        private byte[]? _writerScratch;

        public PortConnection(string portName, int baudRate, PortModes mode)
        {
            Mode = mode;
            try
            {
                _serialPort = mode == PortModes.DMX
                                  ? new SerialPort(portName, 250000, Parity.None, 8, StopBits.Two)
                                  : new SerialPort(portName, baudRate) { ReadTimeout = 1000, WriteTimeout = 500 };
                _serialPort.Open();

                if (mode == PortModes.Standard)
                {
                    _keepRunning = true;
                    // Reader thread is started lazily in AddSubscriber when the first receiver
                    // is added. Output-only consumers (e.g. WLedSerialOutput) pass receiver=null
                    // and never pay the per-second TimeoutException cost from ReadLine.
                    _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = $"SerialWriter_{portName}" };
                    _writerThread.Start();
                }
            }
            catch (Exception e)
            {
                Log.Error($"Failed to open port {portName} in {mode} mode: {e.Message}");
                _serialPort?.Dispose();
                throw;
            }
        }

        public void SendDmxFrame(byte[] dmxFrame)
        {
            if (Mode != PortModes.DMX || !_serialPort.IsOpen) return;
            try
            {
                _serialPort.BreakState = true;
                Thread.Sleep(1);
                _serialPort.BreakState = false;
                Thread.Sleep(1);
                _serialPort.Write(dmxFrame, 0, dmxFrame.Length);
            }
            catch (Exception e) { Log.Warning($"DMX write error: {e.Message}"); }
        }

        public void AddSubscriber(object owner, ISerialReceiver? receiver)
        {
            _subscribers.Add(owner);
            if (receiver == null || _receivers.Contains(receiver)) return;
            _receivers.Add(receiver);
            receiver.SetStatus($"Connected to {_serialPort.PortName}.", IStatusProvider.StatusLevel.Success);

            // Start the reader thread on first receiver. Output-only consumers never trigger this.
            if (Mode == PortModes.Standard && _workerThread == null && _keepRunning)
            {
                _workerThread = new Thread(StandardReadLoop) { IsBackground = true, Name = $"SerialReader_{_serialPort.PortName}" };
                _workerThread.Start();
            }
        }

        public void RemoveSubscriber(object owner)
        {
            _subscribers.Remove(owner);
            if (owner is ISerialReceiver receiver) _receivers.Remove(receiver);
        }

        public void WriteLine(string data) { if (Mode == PortModes.Standard) try { if (IsOpen) _serialPort.WriteLine(data); } catch (Exception e) { Log.Warning($"Serial write error: {e.Message}"); } }
        public void Write(string data) { if (Mode == PortModes.Standard) try { if (IsOpen) _serialPort.Write(data); } catch (Exception e) { Log.Warning($"Serial write error: {e.Message}"); } }

        // Latest-wins enqueue. Real serial write happens on the writer thread,
        // so the caller is not blocked by ~13 ms of TX time at 115200 baud.
        public bool WriteBytes(byte[] data, int length)
        {
            if (Mode != PortModes.Standard || !_keepRunning || !IsOpen)
                return false;

            lock (_frameLock)
            {
                if (_pendingFrame == null || _pendingFrame.Length < length)
                    _pendingFrame = new byte[length];
                Buffer.BlockCopy(data, 0, _pendingFrame, 0, length);
                _pendingFrameLength = length;
            }
            _frameReady.Set();
            return true;
        }

        private void WriterLoop()
        {
            while (_keepRunning)
            {
                _frameReady.WaitOne();
                if (!_keepRunning)
                    break;

                int length;
                lock (_frameLock)
                {
                    length = _pendingFrameLength;
                    if (length == 0 || _pendingFrame == null)
                        continue;
                    if (_writerScratch == null || _writerScratch.Length < length)
                        _writerScratch = new byte[length];
                    Buffer.BlockCopy(_pendingFrame, 0, _writerScratch, 0, length);
                    _pendingFrameLength = 0;
                }

                try
                {
                    if (_serialPort.IsOpen)
                    {
                        if (DebugLogSlowWrites)
                        {
                            var startTicks = Stopwatch.GetTimestamp();
                            _serialPort.Write(_writerScratch, 0, length);
                            var elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                            if (elapsedMs > 0.5)
                                Log.Debug($"Serial writer thread: _serialPort.Write took {elapsedMs:F2}ms ({length}B)");
                        }
                        else
                        {
                            _serialPort.Write(_writerScratch, 0, length);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"Serial write error: {e.Message}");
                    _keepRunning = false;
                    // Close the underlying port so IsOpen / IsAlive reflect the failure
                    // and the caller's auto-reconnect path can do its work without
                    // having to detect a zombie connection out-of-band.
                    try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
                    break;
                }
            }
        }

        private void StandardReadLoop()
        {
            while (_keepRunning)
            {
                try
                {
                    var line = _serialPort.ReadLine();
                    foreach (var receiver in _receivers) receiver.ReceiveLine(line);
                }
                catch (TimeoutException) { /* Expected */ }
                catch (Exception) { _keepRunning = false; }
            }
        }

        public void Dispose()
        {
            _keepRunning = false;
            _frameReady.Set();
            // Writer responds to _frameReady immediately, so 20 ms is plenty.
            _writerThread?.Join(20);
            // Reader is blocked in ReadLine() with a 1 s timeout; capping at 50 ms keeps
            // disposal snappy on the main thread. The thread is IsBackground=true so it
            // dies with the process if it outlives us by the timeout.
            _workerThread?.Join(50);
            _frameReady.Dispose();

            // The USB device may have already vanished (cable yanked / ESP unplugged).
            // SerialPort.Close()/Dispose() then throws IOException from the kernel; swallow it.
            try
            {
                if (_serialPort.IsOpen) _serialPort.Close();
            }
            catch (Exception e) { Log.Debug($"Serial port close failed (device gone?): {e.Message}"); }
            try { _serialPort.Dispose(); }
            catch (Exception e) { Log.Debug($"Serial port dispose failed (device gone?): {e.Message}"); }
        }
    }
}