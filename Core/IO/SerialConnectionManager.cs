#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
#if PLATFORM_WINDOWS
using System.Management;
#endif
using System.Text.RegularExpressions;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;

namespace T3.Core.IO;

public enum PortModes { Standard, DMX }

public interface ISerialReceiver
{
    void ReceiveLine(string line);
    void SetStatus(string message, IStatusProvider.StatusLevel level);
}

public static class SerialConnectionManager
{
    private static readonly Dictionary<string, PortConnection> _connections = new();
    private static readonly object _lock = new();

    #region Connection Management
    public static void Register(object owner, string portName, int baudRate, ISerialReceiver? receiver, PortModes mode)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(portName)) return;

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
    #endregion

    #region Port Scanning
    private static List<string>? _cachedPortList;
    private static readonly Stopwatch _portListCacheStopwatch = new();
    private const int CacheDurationMs = 2000;
    private static readonly Regex _comPortRegex = new(@"\((COM\d+)\)", RegexOptions.Compiled);

    public static List<string> GetAvailableSerialPortsWithDescriptions()
    {
        if (_cachedPortList != null && _portListCacheStopwatch.IsRunning && _portListCacheStopwatch.ElapsedMilliseconds < CacheDurationMs) return _cachedPortList;
        var portList = new List<string>();
#if PLATFORM_WINDOWS
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
#else
        portList.AddRange(SerialPort.GetPortNames());
#endif
        _cachedPortList = portList.OrderBy(s => s).ToList();
        _portListCacheStopwatch.Restart();
        return _cachedPortList;
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
        public int SubscriberCount => _subscribers.Count;
        public readonly PortModes Mode;

        private readonly SerialPort _serialPort;
        private readonly Thread? _workerThread;
        private volatile bool _keepRunning;
        private readonly HashSet<object> _subscribers = new();
        private readonly List<ISerialReceiver> _receivers = new();

        public PortConnection(string portName, int baudRate, PortModes mode)
        {
            Mode = mode;
            try
            {
                _serialPort = mode == PortModes.DMX
                                  ? new SerialPort(portName, 250000, Parity.None, 8, StopBits.Two)
                                  : new SerialPort(portName, baudRate) { ReadTimeout = 1000 };
                _serialPort.Open();

                if (mode == PortModes.Standard)
                {
                    _keepRunning = true;
                    _workerThread = new Thread(StandardReadLoop) { IsBackground = true, Name = $"SerialReader_{portName}" };
                    _workerThread.Start();
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
        }

        public void RemoveSubscriber(object owner)
        {
            _subscribers.Remove(owner);
            if (owner is ISerialReceiver receiver) _receivers.Remove(receiver);
        }

        public void WriteLine(string data) { if (Mode == PortModes.Standard) try { if (IsOpen) _serialPort.WriteLine(data); } catch (Exception e) { Log.Warning($"Serial write error: {e.Message}"); } }
        public void Write(string data) { if (Mode == PortModes.Standard) try { if (IsOpen) _serialPort.Write(data); } catch (Exception e) { Log.Warning($"Serial write error: {e.Message}"); } }

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
            _workerThread?.Join(100);
            if (_serialPort.IsOpen) _serialPort.Close();
            _serialPort.Dispose();
        }
    }
}