using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Rug.Osc;
using T3.Core.Logging;

namespace T3.IoServices;

public static class OscConnectionManager
{
    public static void RegisterConsumer(IOscConsumer consumer, int port)
    {
        var group = CreateOrGetReceiverForPort(port);
        group?.Consumers.Add(consumer);
    }

    public static void UnregisterConsumer(IOscConsumer consumer)
    {
        lock (_groupsByPort)
        {
            var foundConsumer = false;

            foreach (var portGroup in _groupsByPort.Values)
            {
                if (!portGroup.Consumers.Contains(consumer))
                    continue;

                foundConsumer = true;
                var shouldCloseGroup = portGroup.Consumers.Count == 1;
                if (shouldCloseGroup)
                {
                    // Log.Debug($"Closing OSC port because no more listeners {portGroup.Port}");
                    try
                    {
                        portGroup.Stop();
                    }
                    catch (Exception e)
                    {
                        Log.Debug("Closing OSC Port failed" + e.Message);
                    }

                    _groupsByPort.Remove(portGroup.Port);
                }
                else
                {
                    portGroup.Consumers.Remove(consumer);
                }

                break;
            }

            if (!foundConsumer)
            {
                Log.Error("Attempted to unregister a non-registered OSC consumer?");
            }
        }
    }

    private static PortGroup CreateOrGetReceiverForPort(int port)
    {
        if (port < 0 || port > 65535)
            return null;

        if (_groupsByPort.TryGetValue(port, out var receiver))
            return receiver;

        var newGroup = new PortGroup(port);
        _groupsByPort.Add(port, newGroup);
        return newGroup;
    }

    public interface IOscConsumer
    {
        void ProcessMessage(OscMessage msg);
    }

    public static bool TryGetScannedAddressesForPort(int port, out Dictionary<string, string> addresses)
    {
        if (_groupsByPort.TryGetValue(port, out var group))
        {
            addresses = group.ScannedAddresses;
            return true;
        }

        addresses = null;
        return false;
    }

    private static readonly Dictionary<int, PortGroup> _groupsByPort = new();

    private class PortGroup
    {
        public int Port { get; }

        public readonly Dictionary<string, string> ScannedAddresses = new();
        public readonly HashSet<IOscConsumer> Consumers = new();
        private readonly OscReceiver _receiver;

        public PortGroup(int listenPort)
        {
            Port = listenPort;
            _receiver = new OscReceiver(listenPort);
            try
            {
                _receiver.Connect();
                StartListening();
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to open OSC connection on port {listenPort}: {e.Message}");
                Stop();
            }
        }

        // Listen for OSC messages asynchronously
        private async void StartListening()
        {
            var listenTask = ListenForMessagesAsync();
            await listenTask.ContinueWith(task =>
                                          {
                                              // This block executes when ListenForMessagesAsync completes
                                              if (task.IsFaulted)
                                              {
                                                  Log.Warning($"Error while listening for OSC messages on port {Port}: {task.Exception?.Flatten().Message}");
                                              }
                                              else
                                              {
                                                  Stop();
                                              }
                                          }, TaskScheduler.FromCurrentSynchronizationContext()); // 
        }

        private async Task ListenForMessagesAsync()
        {
            try
            {
                while (_receiver is { State: OscSocketState.Connected })
                {
                    while (_receiver.TryReceive(out var packet))
                    {
                        switch (packet)
                        {
                            case OscBundle bundle:
                                foreach (var bundleContent in bundle)
                                {
                                    if (bundleContent is not OscMessage bundleMessage)
                                        continue;

                                    KeepMessageAddress(bundleMessage);
                                    ForwardMessage(bundleMessage);
                                }

                                break;

                            case OscMessage message:
                                KeepMessageAddress(message);
                                ForwardMessage(message);
                                break;
                        }
                    }

                    // Wait a little to avoid CPU overuse
                    await Task.Delay(2);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Error while processing OSC message: {e.Message}");
            }
        }

        public void Stop()
        {
            if (_receiver == null)
                return;

            if (_receiver.State == OscSocketState.Connected)
            {
                Log.Warning("Closing OSC Receiver. " + Port);
                _receiver.Close();
            }

            _receiver?.Dispose();
        }

        private void ForwardMessage(OscMessage message)
        {
            foreach (var consumer in Consumers)
            {
                consumer.ProcessMessage(message);
            }
        }

        private void KeepMessageAddress(OscMessage packet)
        {
            if (ScannedAddresses.ContainsKey(packet.Address))
                return;

            var sb = new StringBuilder();
            foreach (var arg in packet)
            {
                var v = arg switch
                            {
                                float  => "f",
                                int    => "i",
                                bool   => "b",
                                string => "s",
                                double => "d",
                                _      => "?"
                            };
                sb.Append(v);
            }

            ScannedAddresses[packet.Address] = sb.ToString();
        }
    }

    public static bool TryGetFloatFromMessagePart(object arg, out float value)
    {
        value = arg switch
                    {
                        float f  => f,
                        int i    => i,
                        bool b   => b ? 1f : 0f,
                        string s => float.TryParse(s, out var f) ? f : float.NaN,
                        double d => (float)d,
                        _        => float.NaN
                    };
        return !float.IsNaN(value);
    }

    public static string BuildMessageComponentPath(OscMessage msg, int index)
    {
        const string channels = "xyzw";
        var suffix = index < 4 ? channels[index].ToString() : index.ToString();
        return msg.Address + "." + suffix;
    }

    public static string BuildMessageComponentPath(OscMessage msg)
    {
        return msg.Address;
    }
}