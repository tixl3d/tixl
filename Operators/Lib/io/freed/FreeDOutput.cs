#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;
using T3.Core.Utils;

namespace Lib.io.freed;

[Guid("a9b8c7d6-e5f4-4210-9876-54321fedcba0")]
internal sealed class FreeDOutput : Instance<FreeDOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    public FreeDOutput()
    {
        Command.UpdateAction = Update;
        _writer = new BinaryWriter(_packetStream, Encoding.ASCII, true);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);
        var shouldConnect = Connect.GetValue(context);
        var localIp = LocalIpAddress.GetValue(context)  ?? string.Empty;
        var targetIp = TargetIpAddress.GetValue(context) ?? string.Empty;
        var targetPort = TargetPort.GetValue(context);

        var settingsChanged = shouldConnect != _lastConnectState || localIp != _lastLocalIp || targetIp != _lastTargetIp || targetPort != _lastTargetPort;
        if (settingsChanged)
        {
            CloseSocket();
            if (shouldConnect) OpenSocket(localIp, targetIp, targetPort);
            _lastConnectState = shouldConnect;
            _lastLocalIp = localIp;
            _lastTargetIp = targetIp;
            _lastTargetPort = targetPort;
        }

        IsConnected.Value = _udpClient != null;
        if (_udpClient == null) return;

        if (MathUtils.WasTriggered(SendTrigger.GetValue(context), ref _sendTrigger) || SendOnChange.GetValue(context))
        {
            var cameraId = (byte)CameraId.GetValue(context).Clamp(0, 255);
            var rotation = Rotation.GetValue(context);
            var position = Position.GetValue(context);

            // Clamp values to their valid FreeD ranges
            var focus = Math.Clamp(Focus.GetValue(context), 0, 0xFFFFFF);
            var zoom = Math.Clamp(Zoom.GetValue(context), 0, 0xFFFFFF);
            var user = Math.Clamp(User.GetValue(context), 0, 0xFFFF);

            var packetBytes = BuildFreeDPacket(cameraId, rotation, position, zoom, focus, user);
            SendPacket(packetBytes, cameraId);
            SendTrigger.SetTypedInputValue(false);
        }
    }

    private void SendPacket(byte[] packetBytes, byte cameraId)
    {
        if (_udpClient == null || _targetEndPoint == null) return;

        // Fire and forget
        Task.Run(() =>
                 {
                     try
                     {
                         _udpClient.Send(packetBytes, packetBytes.Length, _targetEndPoint);
                         if (_printToLog) Log.Debug($"Sent FreeD packet for Camera ID {cameraId}.", this);
                     }
                     catch (Exception e)
                     {
                         SetStatus($"UDP send error: {e.Message}", IStatusProvider.StatusLevel.Warning);
                     }
                 });

        SetStatus($"Sent FreeD packet for Camera ID {cameraId}.", IStatusProvider.StatusLevel.Success);
    }

    private byte[] BuildFreeDPacket(byte cameraId, Vector3 rotation, Vector3 position, int zoom, int focus, int user)
    {
        _packetStream.SetLength(0);
        _writer.Write(FreeDIdentifier);
        _writer.Write(cameraId);

        WriteInt24BigEndian(_writer, (int)MathF.Round(rotation.X * AngleScale)); // Pan
        WriteInt24BigEndian(_writer, (int)MathF.Round(rotation.Y * AngleScale)); // Tilt
        WriteInt24BigEndian(_writer, (int)MathF.Round(rotation.Z * AngleScale)); // Roll

        WriteInt24BigEndian(_writer, (int)MathF.Round(position.X * PositionToMeterScale)); // PosX
        WriteInt24BigEndian(_writer, (int)MathF.Round(position.Y * PositionToMeterScale)); // PosY
        WriteInt24BigEndian(_writer, (int)MathF.Round(position.Z * PositionToMeterScale)); // PosZ

        WriteInt24BigEndian(_writer, zoom); // Zoom
        WriteInt24BigEndian(_writer, focus); // Focus 

        _writer.Write((byte)((user >> 8) & 0xFF)); // Reserved / User (16-bit)
        _writer.Write((byte)(user & 0xFF));

        var packet = _packetStream.ToArray();
        packet[packet.Length - 1] = CalculateFreeDChecksum(packet, packet.Length - 1);

        return packet;
    }

    private static void WriteInt24BigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static byte CalculateFreeDChecksum(byte[] data, int length)
    {
        var sum = data.Take(length).Aggregate(0u, (current, val) => current + val);
        return (byte)(0x40u - sum);
    }

    private void OpenSocket(string localIpStr, string targetIpStr, int targetPort)
    {
        if (!IPAddress.TryParse(localIpStr, out var localIp))
        {
            SetStatus($"Invalid Local IP '{localIpStr}'.", IStatusProvider.StatusLevel.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(targetIpStr))
            targetIpStr = "255.255.255.255";

        if (!IPAddress.TryParse(targetIpStr, out var targetIp))
        {
            SetStatus($"Invalid Target IP '{targetIpStr}'.", IStatusProvider.StatusLevel.Error);
            return;
        }

        try
        {
            _udpClient = new UdpClient(new IPEndPoint(localIp, 0));
            _targetEndPoint = new IPEndPoint(targetIp, targetPort);
            if (_printToLog) Log.Debug($"FreeD Output: Socket bound to {localIp}, targeting {targetIp}:{targetPort}", this);
            SetStatus($"Socket ready, sending to {targetIp}:{targetPort}", IStatusProvider.StatusLevel.Success);
        }
        catch (Exception e)
        {
            SetStatus($"Failed to open socket: {e.Message}", IStatusProvider.StatusLevel.Error);
        }
    }

    private void CloseSocket()
    {
        _udpClient?.Dispose();
        _udpClient = null;
        if (_lastConnectState) SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            CloseSocket();
            _writer.Dispose();
            _packetStream.Dispose();
        }

        _disposed = true;
    }

    #region Status and Dropdown
    private string _statusMessage = "Not connected.";
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    private readonly object _statusLock = new();

    public void SetStatus(string m, IStatusProvider.StatusLevel l)
    {
        lock (_statusLock)
        {
            _statusMessage = m;
            _statusLevel = l;
        }
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        lock (_statusLock)
        {
            return _statusLevel;
        }
    }

    public string GetStatusMessage()
    {
        lock (_statusLock)
        {
            return _statusMessage;
        }
    }

    string ICustomDropdownHolder.GetValueForInput(Guid id)
    {
        return id == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
    {
        if (id != LocalIpAddress.Id)
            return Empty<string>();

        if (_cachedIpAddresses == null)
            _cachedIpAddresses = GetLocalIPv4Addresses().ToList();

        return _cachedIpAddresses;
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (!string.IsNullOrEmpty(s) && i && id == LocalIpAddress.Id) LocalIpAddress.SetTypedInputValue(s.Split(' ')[0]);
    }

    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) yield break;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return ipInfo.Address.ToString();
        }
    }
    #endregion

    #region Fields
    private UdpClient? _udpClient;
    private IPEndPoint? _targetEndPoint;
    private bool _lastConnectState, _sendTrigger, _printToLog, _disposed;
    private string _lastLocalIp = string.Empty, _lastTargetIp = string.Empty;
    private int _lastTargetPort;
    private readonly MemoryStream _packetStream = new(FreeDPacketLength);
    private List<string>? _cachedIpAddresses;
    private readonly BinaryWriter _writer;
    private const int FreeDPacketLength = 29;
    private const byte FreeDIdentifier = 0xD1;
    private const float AngleScale = 32768.0f;
    private const float PositionToMeterScale = 64000.0f;
    #endregion

    #region Inputs & Outputs
    [Output(Guid = "b0a1c2d3-e4f5-4789-aabb-ccddffeeff00")]
    public readonly Slot<Command> Command = new();

    [Output(Guid = "b0a1c2d3-e4f5-4789-aabb-ccddffeeff01")]
    public readonly Slot<bool> IsConnected = new();

    [Input(Guid = "8f9a0b1c-2d3e-4f5a-a6b7-c8d9e0f1a2b3")]
    public readonly InputSlot<bool> Connect = new();

    [Input(Guid = "9a0b1c2d-3e4f-4a6b-b7c8-d9e0f1a2b3c4")]
    public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0");

    [Input(Guid = "a0b1c2d3-e4f5-4a7b-c8d9-e0f1a2b3c4d5")]
    public readonly InputSlot<string> TargetIpAddress = new("127.0.0.1");

    [Input(Guid = "b1c2d3e4-f5a6-4b8c-d9e0-f1a2b3c4d5e6")]
    public readonly InputSlot<int> TargetPort = new(6000);

    [Input(Guid = "d3e4f5a6-b7c8-4d0e-f1a2-b3c4d5e6f7a8")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "a6b7c8d9-e0f1-4a3b-b4c5-d6e7f8a9b0c1")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "c2d3e4f5-a6b7-4c9d-e0f1-a2b3c4d5e6f7")]
    public readonly InputSlot<int> CameraId = new();

    [Input(Guid = "d9e0f1a2-b3c4-4d6e-8f90-123456789012")]
    public readonly InputSlot<int> Focus = new();

    [Input(Guid = "e0f1a2b3-c4d5-4e7f-9012-345678901234")]
    public readonly InputSlot<int> Zoom = new();

    [Input(Guid = "f1a2b3c4-d5e6-4f8a-9123-456789012345")]
    public readonly InputSlot<int> User = new();

    [Input(Guid = "f1a2b3c4-d5e6-4f8a-8f90-123456789014")]
    public readonly InputSlot<bool> SendOnChange = new();

    [Input(Guid = "a2b3c4d5-e6f7-4a9b-8f90-123456789015")]
    public readonly InputSlot<bool> SendTrigger = new();

    [Input(Guid = "b3c4d5e6-f7a8-4b0c-8f90-123456789016")]
    public readonly InputSlot<bool> PrintToLog = new();
    #endregion
}