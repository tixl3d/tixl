#nullable enable
using ArtNet.Packets;
using ArtNet.Sockets;
using T3.Core.Utils;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.artnet;

[Guid("98efc7c8-cafd-45ee-8746-14f37e9f59f8")]
internal sealed class ArtnetOutput : Instance<ArtnetOutput>
,IStatusProvider
{
    [Output(Guid = "499329d0-15e9-410e-9f61-63724dbec937")]
    public readonly Slot<Command> Result = new();

    public ArtnetOutput()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var startUniverse = StartUniverse.GetValue(context);
        var inputValueLists = InputsValues.GetCollectedTypedInputs();
        _enableSending = SendTrigger.GetValue(context);

        var needReconnect = Reconnect.GetValue(context);
        if (needReconnect)
        {
            Reconnect.SetTypedInputValue(false); // Reset input
        }

        needReconnect |= _connectionSettings.UpdateTargetAddress(LocalIpAddress.GetValue(context));
        needReconnect |= _connectionSettings.UpdateSubnetMask(SubNetMask.GetValue(context));
        //needReconnect |= _connectionSettings.UpdateBroadCast(Broadcast.GetValue(context));

        if (needReconnect)
        {
            Log.Debug("Reconnecting...", this);
            _sender?.Close();

            _connected = TryConnectArtNet(_connectionSettings);
        }
        
        SendData(context, startUniverse, inputValueLists);
    }

    /// <summary>
    ///Send DMX data across universes 
    /// </summary>
    private void SendData(EvaluationContext context, int startUniverse, List<Slot<List<int>>> connections)
    {
        if (!_enableSending || !_connected)
            return;

        const int chunkSize = 512;
        int universeIndex = startUniverse;

        foreach (var input in connections)
        {
            var buffer = input.GetValue(context);
            var bufferCount = buffer.Count;

            // Process in chunks
            for (var i = 0; i < bufferCount; i += chunkSize)
            {
                var currentChunkSize = Math.Min(chunkSize, bufferCount - i);
                var dmxData = buffer.Skip(i).Take(currentChunkSize);

                var dmxPacket = new ArtNetDmxPacket
                                    {
                                        DmxData = ConvertListToByteArray(dmxData, currentChunkSize),
                                        Universe = (short)universeIndex
                                    };

                // Sending DMX data
                try
                {
                    _sender?.Send(dmxPacket);
                }
                catch (Exception e)
                {
                    Log.Warning("Failed to send artnet" + e.Message, this);
                }

                universeIndex++;
            }
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        Log.Debug("Disposing...", this);
        _sender?.Dispose();
    }
    
    private bool TryConnectArtNet(ConnectionSettings settings)
    {
        _sender?.Close();
        _sender?.Dispose(); // This might already include closing
        if (!settings.IsValid())
            return false;

        try
        {
            _sender = new ArtNetSocket();
            if (settings.UseBroadcast)
            {
                _sender.EnableBroadcast = true;
            }
            else
            {
                _sender.Open(settings.TargetAddress, settings.SubnetMask);
            }
        }
        catch (Exception e)
        {
            _lastErrorMessage = $"Failed to connect to {settings.TargetAddress} " + e.Message;
            Log.Warning(_lastErrorMessage, this);
            return false;
        }

        return true;
    }

    private static byte[] ConvertListToByteArray(IEnumerable<int> intList, int count)
    {
        var byteArray = new byte[count];

        var index = 0;
        foreach (var v in intList)
        {
            byteArray[index++] = (byte)v.Clamp(0, 255);
        }

        return byteArray;
    }

    private ArtNetSocket? _sender;
    private bool _connected;

    private bool _enableSending;
    private readonly ConnectionSettings _connectionSettings = new();

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }
    
    string? IStatusProvider.GetStatusMessage() => _lastErrorMessage;
    private string? _lastErrorMessage;

        [Input(Guid = "F7520A37-C2D4-41FA-A6BA-A6ED0423A4EC")]
        public readonly MultiInputSlot<System.Collections.Generic.List<int>> InputsValues = new();

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

    
    /// <summary>
    /// Helper class for updating connection settings
    /// </summary>
    private sealed class ConnectionSettings
    {
        public bool UseBroadcast;
        public IPAddress? TargetAddress;
        public IPAddress? SubnetMask = IPAddress.Parse("255.255.255.0");

        public bool IsValid()
        {
            return SubnetMask != null && TargetAddress != null;
        }

        public bool UpdateTargetAddress(string ipString)
        {
            return TrySetIfChanged(ipString, ref TargetAddress, ref _lastTargetAddressString);
        }

        public bool UpdateSubnetMask(string ipString)
        {
            return TrySetIfChanged(ipString, ref SubnetMask, ref _lastSubnetString);
        }

        public bool UpdateBroadCast(bool broadcast)
        {
            var hasChanged = broadcast != UseBroadcast;
            UseBroadcast = broadcast;
            return hasChanged;
        }

        private static bool TrySetIfChanged(string? ipString, ref IPAddress? ipAddress, ref string? lastString)
        {
            if (ipString == lastString)
                return false;

            lastString = ipString;

            if (ipString == null)
            {
                ipAddress = null;
            }
            else
            {
                _ = IPAddress.TryParse(ipString.Trim(), out ipAddress);
            }

            return true;
        }

        private string? _lastTargetAddressString;
        private string? _lastSubnetString;
    }
}