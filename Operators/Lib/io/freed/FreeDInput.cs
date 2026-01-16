#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Lib.io.freed;

[Guid("1f2e3d4c-5b6a-4988-9a0b-c1d2e3f4a5b6")]
internal sealed class FreeDInput : Instance<FreeDInput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    public FreeDInput()
    {
        // All outputs are updated from the same method
        CameraDataAsDict.UpdateAction = Update;
        IsListening.UpdateAction = Update;
        CameraPos.UpdateAction = Update;
        CameraRot.UpdateAction = Update;
        CamerasAsBuffer.UpdateAction = Update;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);
        if (_printToLog) Log.Debug($"FreeD Input: Update. Queue size: {_receivedDataQueue.Count}", this);

        while (_receivedDataQueue.TryDequeue(out var data))
            ParseFreeDDataPacket(data);

        var shouldListen = Listen.GetValue(context);
        var localIp = LocalIpAddress.GetValue(context) ?? string.Empty;
        var port = Port.GetValue(context);

        var settingsChanged = shouldListen != _lastListenState || localIp != _lastLocalIp || port != _lastPort;
        if (settingsChanged)
        {
            StopListening();
            if (shouldListen) StartListening(localIp, port);
            _lastListenState = shouldListen;
            _lastLocalIp = localIp;
            _lastPort = port;
        }

        IsListening.Value = _listenerTask is { Status: TaskStatus.Running };

        var cameraCount = _trackedValues.Count;

        if (cameraCount == 0)
        {
            CamerasAsBuffer.Value = null;
            CameraDataAsDict.Value = new Dict<float>(0.0f);
            SetStatus("Listening, no camera data received yet.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        var dict = new Dict<float>(0.0f);
        if (_pointArrayForGpu == null || _pointArrayForGpu.Length != cameraCount)
            _pointArrayForGpu = new Point[cameraCount];

        var i = 0;
        foreach (var (id, data) in _trackedValues.OrderBy(kvp => kvp.Key))
        {
            var yaw = data.Rotation.X * (MathF.PI / 180f);
            var pitch = data.Rotation.Y * (MathF.PI / 180f);
            var roll = data.Rotation.Z * (MathF.PI / 180f);
            var quaternion = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
            _pointArrayForGpu[i] = new Point
                                       {
                                           Position = data.Position,
                                           Orientation = quaternion,
                                           Scale = new Vector3(data.Focus, data.Zoom, data.User),
                                           F1 = id,
                                           Color = Vector4.One
                                       };

            var basePath = $"/{id}";
            dict[basePath + "/Pan"] = data.Rotation.X;
            dict[basePath + "/Tilt"] = data.Rotation.Y;
            dict[basePath + "/Roll"] = data.Rotation.Z;
            dict[basePath + "/PosX"] = data.Position.X;
            dict[basePath + "/PosY"] = data.Position.Y;
            dict[basePath + "/PosZ"] = data.Position.Z;
            dict[basePath + "/Zoom"] = data.Zoom;
            dict[basePath + "/Focus"] = data.Focus;
            dict[basePath + "/User"] = data.User;
            i++;
        }

        // Update single camera outputs
        var selectedCameraId = CameraId.GetValue(context);
        if (selectedCameraId < 0)
            selectedCameraId = _trackedValues.Keys.Any() ? _trackedValues.Keys.Min() : -1;

        if (selectedCameraId >= 0 && _trackedValues.TryGetValue((byte)selectedCameraId, out var selectedData))
        {
            CameraPos.Value = selectedData.Position;
            CameraRot.Value = selectedData.Rotation;
            Zoom.Value = selectedData.Zoom;
            Focus.Value = selectedData.Focus;
            User.Value = selectedData.User;
        }

        UpdateGpuBuffer(ref _outputBuffer, _pointArrayForGpu);
        CamerasAsBuffer.Value = _outputBuffer;
        CameraDataAsDict.Value = dict;
        SetStatus($"Tracking {cameraCount} cameras.", IStatusProvider.StatusLevel.Success);
    }

    private void StartListening(string localIpStr, int port)
    {
        if (_listenerTask is { Status: TaskStatus.Running }) return;
        _cancellationTokenSource = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(localIpStr, port, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
    }

    private void StopListening()
    {
        if (_listenerTask == null) return;
        if (_printToLog) Log.Debug("FreeD Input: Stopping listener.", this);
        _cancellationTokenSource?.Cancel();
        _udpClient?.Close();
    }

    private async Task ListenLoopAsync(string localIpStr, int port, CancellationToken token)
    {
        try
        {
            var listenIp = IPAddress.Any;
            if (!string.IsNullOrEmpty(localIpStr) && localIpStr != "0.0.0.0 (Any)" && IPAddress.TryParse(localIpStr, out var parsedIp))
                listenIp = parsedIp;

            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(listenIp, port));
            if (_printToLog) Log.Debug($"FreeD Input: Bound to {listenIp}:{port}.", this);

            while (!token.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(token);
                _receivedDataQueue.Enqueue(result.Buffer);
                CamerasAsBuffer.DirtyFlag.Invalidate();
            }
        }
        catch (OperationCanceledException)
        {
            /* Expected */
        }
        catch (Exception e)
        {
            SetStatus($"Listener failed: {e.Message}", IStatusProvider.StatusLevel.Error);
        }
        finally
        {
            _udpClient?.Dispose();
        }
    }

    private void ParseFreeDDataPacket(byte[] data)
    {
        if (data.Length != FreeDPacketLength || data[0] != FreeDIdentifier) return;
        if (data[FreeDPacketLength - 1] != CalculateFreeDChecksum(data, FreeDPacketLength - 1)) return;

        var zoom = ReadUInt24BigEndian(data, 20);
        var focus = ReadUInt24BigEndian(data, 23);
        var user = (data[26] << 8) | data[27];

        var newData = new FreeDCameraData(
                                          CameraId: data[1],
                                          Rotation: new Vector3(ReadInt24BigEndian(data, 2) / AngleScale, ReadInt24BigEndian(data, 5) / AngleScale,
                                                                ReadInt24BigEndian(data, 8) / AngleScale),
                                          Position: new Vector3(ReadInt24BigEndian(data, 11) / PositionToMeterScale,
                                                                ReadInt24BigEndian(data, 14) / PositionToMeterScale,
                                                                ReadInt24BigEndian(data, 17) / PositionToMeterScale),
                                          Zoom: zoom,
                                          Focus: focus,
                                          User: user
                                         );
        _trackedValues[newData.CameraId] = newData;
        if (_printToLog) Log.Debug($"FreeD Input: Parsed data for CameraId {newData.CameraId}. Zoom={zoom}, Focus={focus}, User={user}", this);
    }

    private static int ReadInt24BigEndian(byte[] buffer, int offset)
    {
        var value = (buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2];
        if ((buffer[offset] & 0x80) != 0)
            value |= unchecked((int)0xFF000000); // Sign extend
        return value;
    }

    private static int ReadUInt24BigEndian(byte[] buffer, int offset)
    {
        return (buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2];
    }

    private static byte CalculateFreeDChecksum(byte[] data, int length)
    {
        return (byte)(0x40u - data.Take(length).Aggregate(0u, (sum, val) => sum + val));
    }

    private static void UpdateGpuBuffer(ref BufferWithViews? buffer, Point[] array)
    {
        var num = array.Length;
        if (num != (buffer?.Buffer.Description.SizeInBytes / Point.Stride ?? 0))
        {
            buffer?.Dispose();
            buffer = new BufferWithViews();
            ResourceManager.SetupStructuredBuffer(array, Point.Stride * num, Point.Stride, ref buffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
            ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
        }
        else
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(array, buffer?.Buffer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            StopListening();
            _outputBuffer?.Dispose();
        }

        _disposed = true;
    }

    #region Status and Dropdown
    private string _statusMessage = "Not listening.";
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
        return id == LocalIpAddress.Id ? LocalIpAddress.Value  ?? string.Empty: string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
    {
        if (id != LocalIpAddress.Id)
            return Empty<string>();

        return _cachedIpAddresses ??= GetLocalIPv4Addresses().ToList();
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (!string.IsNullOrEmpty(s) && i && id == LocalIpAddress.Id) LocalIpAddress.SetTypedInputValue(s);
    }

    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        yield return "0.0.0.0 (Any)";
        yield return "127.0.0.1";
        if (!NetworkInterface.GetIsNetworkAvailable()) yield break;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return ipInfo.Address.ToString();
        }
    }
    #endregion

    #region Fields
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenerTask;
    private bool _lastListenState, _printToLog, _disposed;
    private string _lastLocalIp = string.Empty;
    private int _lastPort;
    private readonly ConcurrentQueue<byte[]> _receivedDataQueue = new();
    private readonly ConcurrentDictionary<byte, FreeDCameraData> _trackedValues = new();
    private BufferWithViews? _outputBuffer;
    private Point[]? _pointArrayForGpu;
    private List<string>? _cachedIpAddresses;
    private const int FreeDPacketLength = 29;
    private const byte FreeDIdentifier = 0xD1;
    private const float AngleScale = 32768.0f;
    private const float PositionToMeterScale = 64000.0f;

    private record struct FreeDCameraData(Vector3 Rotation, Vector3 Position, int Zoom, int Focus, byte CameraId, int User);
    #endregion

    #region Inputs & Outputs
    [Output(Guid = "8B9C0D1E-2F3A-4B5C-6D7E-8F9A0B1C2D3E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> CamerasAsBuffer = new();

    [Output(Guid = "2b3c4d5e-6f7a-4b9c-a0b1-c2d3e4f5a6b7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Dict<float>> CameraDataAsDict = new();

    [Output(Guid = "3c4d5e6f-7a8b-9c0d-b1c2-d3e4f5a6b7c8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsListening = new();

    [Output(Guid = "1A2B3C4D-5E6F-7A8B-9C0D-B1C2D3E4F5A6", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> CameraPos = new();

    [Output(Guid = "2B3C4D5E-6F7A-8B9C-0D1E-2F3A4B5C6D7E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> CameraRot = new();

    [Output(Guid = "3C4D5E6F-7A8B-9C0D-1E2F-3A4B5C6D7E8F", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Zoom = new();

    [Output(Guid = "4D5E6F7A-8B9C-0D1E-2F3A-4B5C6D7E8F9A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Focus = new();

    [Output(Guid = "5E6F7A8B-9C0D-1E2F-3A4B-5C6D7E8F9A0B", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> User = new();

    [Input(Guid = "4b5c6d7e-8f9a-4b1c-c2d3-e4f5a6b7c8d9")]
    public readonly InputSlot<bool> Listen = new();

    [Input(Guid = "5c6d7e8f-9a0b-4c2d-d3e4-f5a6b7c8d9e0")]
    public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)");

    [Input(Guid = "6d7e8f9a-0b1c-4d3e-e4f5-a6b7c8d9e0f1")]
    public readonly InputSlot<int> Port = new(6000);

    [Input(Guid = "7e8f9a0b-1c2d-4e5f-a6b7-c8d9e0f1a2b3")]
    public readonly InputSlot<int> CameraId = new(-1);

    [Input(Guid = "8f9a0b1c-2d3e-4f5a-a6b7-c8d9e0f1a2b3")]
    public readonly InputSlot<bool> PrintToLog = new();
    #endregion
}