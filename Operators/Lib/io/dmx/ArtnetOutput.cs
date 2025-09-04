#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.dmx;

[Guid("98efc7c8-cafd-45ee-8746-14f37e9f59f8")]
internal sealed class ArtnetOutput : Instance<ArtnetOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private const int ArtNetPort = 6454; // Standard Art-Net port

    [Output(Guid = "499329d0-15e9-410e-9f61-63724dbec937")]
    public readonly Slot<Command> Result = new();

    // ArtPoll related fields
    private Timer? _artPollTimer;
    private Thread? _artPollListenerThread;
    private volatile bool _isPolling;
    private readonly ConcurrentDictionary<string, string> _discoveredNodes = new();

    public ArtnetOutput()
    {
        Result.UpdateAction = Update;

        // Log local adapter IP addresses for easier setup
        Log.Debug("Available Network Interfaces for Art-Net:");
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Log.Debug($"- {ni.Name}: {ip.Address}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Could not enumerate network interfaces: " + e.Message, this);
        }
    }

    private void Update(EvaluationContext context)
    {
        _enableSending = SendTrigger.GetValue(context);
        var sendSync = SendSync.GetValue(context);
        var sendUnicast = SendUnicast.GetValue(context);

        var settingsChanged = _connectionSettings.Update(
                                                         LocalIpAddress.GetValue(context),
                                                         SubNetMask.GetValue(context),
                                                         TargetIpAddress.GetValue(context),
                                                         sendUnicast
                                                        );

        if (Reconnect.GetValue(context) || settingsChanged)
        {
            Reconnect.SetTypedInputValue(false);
            Log.Debug("Reconnecting Art-Net socket...", this);
            CloseSocket();
            _connected = TryConnectArtNet(_connectionSettings.LocalIp);
        }

        // Handle ArtPoll
        var discoverNodes = PrintArtnetPoll.GetValue(context); // Assuming this is the "discover" trigger
        if (discoverNodes && !_isPolling)
        {
            StartArtPolling();
        }
        else if (!discoverNodes && _isPolling)
        {
            StopArtPolling();
        }

        if (!_enableSending || !_connected || _socket == null)
            return;

        // Send ArtSync packet if requested
        if (sendSync)
        {
            SendArtSync();
        }

        var startUniverse = StartUniverse.GetValue(context);
        var inputValueLists = InputsValues.GetCollectedTypedInputs();
        SendData(context, startUniverse, inputValueLists);
    }

    private void StartArtPolling()
    {
        if (_socket == null)
        {
            Log.Warning("Cannot start ArtPoll: Socket is not connected.", this);
            return;
        }

        Log.Debug("Starting ArtPoll...", this);
        _discoveredNodes.Clear();
        _isPolling = true;

        // Start listener thread
        _artPollListenerThread = new Thread(ListenForArtPollReplies)
        {
            IsBackground = true,
            Name = "ArtNetPollListener"
        };
        _artPollListenerThread.Start();

        // Start timer to send polls
        _artPollTimer = new Timer(state => SendArtPoll(), null, 0, 3000); // Send every 3 seconds
    }

    private void StopArtPolling()
    {
        if (!_isPolling) return;

        Log.Debug("Stopping ArtPoll.", this);
        _isPolling = false;

        _artPollTimer?.Dispose();
        _artPollTimer = null;

        // The socket is now closed in Dispose or Reconnect,
        // which will terminate the listener thread.
        // We can optionally join the thread here to wait for it to finish
        _artPollListenerThread?.Join(200);
        _artPollListenerThread = null;
    }

    private void SendArtPoll()
    {
        if (_socket == null || !_isPolling || _connectionSettings.LocalIp == null) return;

        using var memoryStream = new MemoryStream(14);
        using var writer = new BinaryWriter(memoryStream);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("Art-Net"));
        writer.Write((byte)0);      // Null terminator
        writer.Write((short)0x2000); // OpCode (OpPoll)
        writer.Write((byte)0);      // ProtVerHi
        writer.Write((byte)14);     // ProtVerLo
        writer.Write((byte)2);      // TalkToMe: Send ArtPollReply whenever Node conditions change
        writer.Write((byte)0);      // Priority

        var packetBytes = memoryStream.ToArray();
        try
        {
            // Always send a broadcast poll to discover all nodes on the network segment
            if (IPAddress.TryParse(SubNetMask.GetValue(new EvaluationContext()), out var subnet))
            {
                var broadcastAddress = CalculateBroadcastAddress(_connectionSettings.LocalIp, subnet);
                if (broadcastAddress != null)
                {
                    var broadcastEndPoint = new IPEndPoint(broadcastAddress, ArtNetPort);
                    _socket.SendTo(packetBytes, broadcastEndPoint);
                }
            }
            else
            {
                // Fallback to general broadcast if subnet is invalid
                _socket.SendTo(packetBytes, new IPEndPoint(IPAddress.Broadcast, ArtNetPort));
            }
        }
        catch (Exception e)
        {
            if (_isPolling)
                Log.Warning($"Failed to send ArtPoll: {e.Message}", this);
        }
    }

    private void ListenForArtPollReplies()
    {
        var remoteEP = new IPEndPoint(IPAddress.Any, 0) as EndPoint;
        var buffer = new byte[1024];

        while (_isPolling)
        {
            try
            {
                if (_socket == null || _socket.Available == 0)
                {
                    Thread.Sleep(10); // Avoid tight loop
                    continue;
                }

                var receivedBytes = _socket.ReceiveFrom(buffer, ref remoteEP);

                if (receivedBytes < 238) continue; // Minimum length of an ArtPollReply

                if (System.Text.Encoding.ASCII.GetString(buffer, 0, 8) != "Art-Net\0" ||
                    buffer[8] != 0x00 || buffer[9] != 0x21)
                {
                    continue;
                }

                var ipAddress = new IPAddress(new[] { buffer[10], buffer[11], buffer[12], buffer[13] });
                var shortName = System.Text.Encoding.ASCII.GetString(buffer, 26, 18).TrimEnd('\0');
                var ipString = ipAddress.ToString();

                var displayName = string.IsNullOrWhiteSpace(shortName) ? ipString : shortName;
                _discoveredNodes[ipString] = $"{displayName} ({ipString})";

                Log.Debug($"ArtPollReply from {ipString} | Name: '{shortName}'", this);
            }
            catch (SocketException)
            {
                if (_isPolling) break; // Exit if socket is closed while polling
            }
            catch (Exception e)
            {
                if (_isPolling)
                {
                    Log.Warning($"Error while listening for ArtPollReply: {e.Message}", this);
                    Thread.Sleep(1000);
                }
            }
        }
        Log.Debug("Stopped listening for ArtPoll replies.", this);
    }


    private void SendArtSync()
    {
        if (_socket == null || _connectionSettings.TargetEndPoint == null) return;

        using var memoryStream = new MemoryStream(18); // ArtSync packet is small
        using var writer = new BinaryWriter(memoryStream);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("Art-Net"));
        writer.Write((byte)0);      // Null terminator
        writer.Write((short)0x5200); // OpCode (OpSync)
        writer.Write((byte)0);      // ProtVerHi
        writer.Write((byte)14);     // ProtVerLo
        writer.Write((byte)0);      // Aux1
        writer.Write((byte)0);      // Aux2

        var packetBytes = memoryStream.ToArray();
        try
        {
            _socket.SendTo(packetBytes, _connectionSettings.TargetEndPoint);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to send ArtSync: {e.Message}", this);
            _connected = false; // Trigger reconnect on next frame
        }
    }

    private void SendData(EvaluationContext context, int startUniverse, List<Slot<List<int>>> connections)
    {
        if (_socket == null || _connectionSettings.TargetEndPoint == null)
            return;

        const int chunkSize = 512;
        var universeIndex = startUniverse;

        foreach (var input in connections)
        {
            var buffer = input.GetValue(context);
            if (buffer == null) continue;
            var bufferCount = buffer.Count;

            for (var i = 0; i < bufferCount; i += chunkSize)
            {
                var remaining = bufferCount - i;
                var chunkCount = Math.Min(remaining, chunkSize);
                if (chunkCount == 0) continue;

                var sendLength = Math.Max(2, chunkCount); // ArtDmx data length must be even
                if (sendLength % 2 != 0) sendLength++;

                var dmxData = new byte[sendLength];
                for (var j = 0; j < chunkCount; j++)
                {
                    dmxData[j] = (byte)buffer[i + j].Clamp(0, 255);
                }

                try
                {
                    using var memoryStream = new MemoryStream(18 + dmxData.Length);
                    using var writer = new BinaryWriter(memoryStream);

                    // Write Art-Net header
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("Art-Net"));
                    writer.Write((byte)0);      // Null terminator
                    writer.Write((short)0x5000); // OpCode (OpDmx)
                    writer.Write((byte)0);      // ProtVerHi
                    writer.Write((byte)14);     // ProtVerLo
                    writer.Write(_sequenceNumber++); // Sequence
                    writer.Write((byte)0);      // Physical
                    writer.Write((ushort)universeIndex); // Universe
                    writer.Write((byte)(dmxData.Length >> 8));   // Length Hi
                    writer.Write((byte)(dmxData.Length & 0xFF)); // Length Lo

                    // Write DMX data
                    writer.Write(dmxData);

                    var packetBytes = memoryStream.ToArray();
                    _socket.SendTo(packetBytes, _connectionSettings.TargetEndPoint);
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to send Art-Net to universe {universeIndex}: {e.Message}", this);
                    _connected = false; // Trigger reconnect on next frame
                    return; // Stop sending data this frame
                }

                universeIndex++;
            }
        }
    }

    public void Dispose()
    {
        StopArtPolling();
        CloseSocket();
    }

    private void CloseSocket()
    {
        _socket?.Close();
        _socket = null;
        _connected = false;
    }

    private bool TryConnectArtNet(IPAddress? localIp)
    {
        if (localIp == null)
        {
            _lastErrorMessage = "Local IP Address is not valid. Please select a valid network adapter.";
            Log.Warning(_lastErrorMessage, this);
            return false;
        }

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            // Non-blocking for listener thread
            _socket.Blocking = false;
            _socket.Bind(new IPEndPoint(localIp, ArtNetPort));
            _lastErrorMessage = null;
            _connected = true;
            return true;
        }
        catch (Exception e)
        {
            _lastErrorMessage = $"Failed to bind socket to {localIp}:{ArtNetPort}: {e.Message}";
            Log.Warning(_lastErrorMessage, this);
            CloseSocket();
            return false;
        }
    }

    private static IPAddress? CalculateBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || subnetMask.AddressFamily != AddressFamily.InterNetwork)
            return null;

        byte[] ipAddressBytes = address.GetAddressBytes();
        byte[] subnetMaskBytes = subnetMask.GetAddressBytes();

        if (ipAddressBytes.Length != subnetMaskBytes.Length)
            return null;

        byte[] broadcastAddress = new byte[ipAddressBytes.Length];
        for (int i = 0; i < broadcastAddress.Length; i++)
        {
            broadcastAddress[i] = (byte)(ipAddressBytes[i] | (subnetMaskBytes[i] ^ 255));
        }
        return new IPAddress(broadcastAddress);
    }

    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        yield return "127.0.0.1"; // Loopback
        if (!NetworkInterface.GetIsNetworkAvailable()) yield break;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return ipInfo.Address.ToString();
        }
    }

    private Socket? _socket;
    private bool _connected;
    private bool _enableSending;
    private byte _sequenceNumber;
    private readonly ConnectionSettings _connectionSettings = new();
    private string? _lastErrorMessage;

    public IStatusProvider.StatusLevel GetStatusLevel() => _connected ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    public string? GetStatusMessage() => _lastErrorMessage;

    #region ICustomDropdownHolder implementation
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == LocalIpAddress.Id) return LocalIpAddress.Value;
        if (inputId == TargetIpAddress.Id) return TargetIpAddress.Value;
        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == LocalIpAddress.Id)
        {
            foreach (var address in GetLocalIPv4Addresses())
            {
                yield return address;
            }
        }
        else if (inputId == TargetIpAddress.Id)
        {
            if (!_isPolling && _discoveredNodes.IsEmpty)
            {
                yield return "Enable 'Discover Nodes' to search...";
            }
            else if (_isPolling && _discoveredNodes.IsEmpty)
            {
                yield return "Searching for nodes...";
            }
            else
            {
                foreach (var nodeName in _discoveredNodes.Values.OrderBy(name => name))
                {
                    yield return nodeName;
                }
            }
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem) return;

        var finalIp = selected;
        // This regex extracts the IP address from the format "DisplayName (192.168.1.10)"
        if (inputId == TargetIpAddress.Id)
        {
            var match = Regex.Match(selected, @"\(([^)]*)\)");
            if (match.Success)
            {
                finalIp = match.Groups[1].Value;
            }
        }

        if (inputId == LocalIpAddress.Id)
        {
            LocalIpAddress.SetTypedInputValue(finalIp);
        }
        else if (inputId == TargetIpAddress.Id)
        {
            TargetIpAddress.SetTypedInputValue(finalIp);
        }
    }
    #endregion

    [Input(Guid = "F7520A37-C2D4-41FA-A6BA-A6ED0423A4EC")]
    public readonly MultiInputSlot<List<int>> InputsValues = new();

    [Input(Guid = "34aeeda5-72b0-4f13-bfd3-4ad5cf42b24f")]
    public readonly InputSlot<int> StartUniverse = new();

    [Input(Guid = "fcbfe87b-b8aa-461c-a5ac-b22bb29ad36d")]
    public readonly InputSlot<string> LocalIpAddress = new();

    [Input(Guid = "35A5EFD8-B670-4F2D-BDE0-380789E85E0C")]
    public readonly InputSlot<string> SubNetMask = new();

    [Input(Guid = "168d0023-554f-46cd-9e62-8f3d1f564b8d")]
    public readonly InputSlot<bool> SendTrigger = new();

    [Input(Guid = "73babdb1-f88f-4e4d-aa3f-0536678b0793")]
    public readonly InputSlot<bool> Reconnect = new();

    [Input(Guid = "d293bb33-2fba-4048-99b8-86aa15a478f2")]
    public readonly InputSlot<bool> SendSync = new();

    [Input(Guid = "7c15da5f-cfa1-4339-aceb-4ed0099ea041")]
    public readonly InputSlot<bool> SendUnicast = new();

    [Input(Guid = "0fc76369-788a-4ffe-9dde-8eea5f10cf32")]
    public readonly InputSlot<string> TargetIpAddress = new();

    [Input(Guid = "65fb88ec-5772-4973-bd8b-bb2cb9f557e7")]
    public readonly InputSlot<bool> PrintArtnetPoll = new();

    private sealed class ConnectionSettings
    {
        public IPAddress? LocalIp { get; private set; }
        public IPEndPoint? TargetEndPoint { get; private set; }

        private string? _lastLocalIpStr;
        private string? _lastSubnetStr;
        private string? _lastTargetIpStr;
        private bool _lastSendUnicast;

        public bool Update(string localIpStr, string subnetStr, string targetIpStr, bool sendUnicast)
        {
            bool changed = false;

            if (_lastLocalIpStr != localIpStr)
            {
                _lastLocalIpStr = localIpStr;
                IPAddress.TryParse(localIpStr, out var parsedLocalIp);
                LocalIp = parsedLocalIp;
                changed = true;
            }

            if (changed || _lastSubnetStr != subnetStr || _lastTargetIpStr != targetIpStr || _lastSendUnicast != sendUnicast)
            {
                _lastSubnetStr = subnetStr;
                _lastTargetIpStr = targetIpStr;
                _lastSendUnicast = sendUnicast;

                IPAddress? targetIp = null;
                if (sendUnicast)
                {
                    IPAddress.TryParse(targetIpStr, out targetIp);
                }
                else
                {
                    if (LocalIp != null && IPAddress.TryParse(subnetStr, out var subnetMask))
                    {
                        targetIp = CalculateBroadcastAddress(LocalIp, subnetMask);
                    }
                }

                if (targetIp != null)
                {
                    TargetEndPoint = new IPEndPoint(targetIp, ArtNetPort);
                }
                else
                {
                    TargetEndPoint = null;
                }
            }

            return changed;
        }
    }
}