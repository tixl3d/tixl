#nullable enable
using System.Net.Sockets;
using System.Threading;
using System.Net.NetworkInformation;
// ReSharper disable SuggestVarOrType_BuiltInTypes

namespace Lib.io.posistage
{


    [Guid("A9E8D7C6-B5A4-4E1A-8D0F-3C5A7B9E2D1F")]
    internal sealed class PosiStageInput : Instance<PosiStageInput>, IStatusProvider, ICustomDropdownHolder
    {
        [Output(Guid = "C8C4D0F7-C06E-4B1A-9D6C-6F5E4D3C2B1B", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<BufferWithViews?> TrackersAsBuffer = new();

        [Output(Guid = "E4B3A2C1-D0E9-4F8A-7B6C-5D4E3F2A1B0C", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Dict<float>> TrackersAsDict = new();

        [Output(Guid = "9C4D3558-1584-422E-A59B-D08D23E45242")]
        public readonly Slot<bool> IsListening = new();

        private record struct TrackerData(Vector3 Position, Vector3 AxisAngle);
        private readonly ConcurrentDictionary<ushort, TrackerData> _trackedValues = new();
        private BufferWithViews? _outputBuffer;
        private PsnPoint[]? _pointArrayForGpu;

        public PosiStageInput()
        {
            TrackersAsBuffer.UpdateAction = Update;
            TrackersAsDict.UpdateAction = Update;
            IsListening.UpdateAction = Update;
        }

        private UdpClient? _udpClient;
        private Thread? _listenerThread;
        private volatile bool _runListener;
        private bool _lastListenState;
        private bool _printToLog;
        private string _lastLocalIp = string.Empty;
        private string _lastMulticastIp = string.Empty;
        private int _lastPort;

        private void Update(EvaluationContext context)
        {
            _printToLog = PrintToLog.GetValue(context);
            var shouldListen = Listen.GetValue(context);
            var localIp = LocalIpAddress.GetValue(context) ?? string.Empty;
            var multicastIp = MulticastIpAddress.GetValue(context) ?? string.Empty;
            var port = Port.GetValue(context);

            var settingsChanged = shouldListen != _lastListenState || localIp != _lastLocalIp || multicastIp != _lastMulticastIp || port != _lastPort;

            if (settingsChanged)
            {
                StopListening();
                if (shouldListen) StartListening(localIp, multicastIp, port);
                _lastListenState = shouldListen;
                _lastLocalIp = localIp;
                _lastMulticastIp = multicastIp;
                _lastPort = port;
            }

            IsListening.Value = _runListener;
            int trackerCount = _trackedValues.Count;

            if (trackerCount == 0)
            {
                TrackersAsBuffer.Value = null;
                TrackersAsDict.Value = new Dict<float>(0.0f);
                SetStatus("No trackers received.", IStatusProvider.StatusLevel.Notice);
                return;
            }

            var dict = new Dict<float>(0.0f);

            if (_pointArrayForGpu == null || _pointArrayForGpu.Length != trackerCount)
                _pointArrayForGpu = new PsnPoint[trackerCount];

            int i = 0;
            // Order by ID to ensure a consistent layout in the buffer from frame to frame
            foreach (var (id, data) in _trackedValues.OrderBy(kvp => kvp.Key))
            {
                var quaternion = ToQuaternion(data.AxisAngle);
                _pointArrayForGpu[i] = new PsnPoint
                {
                    F1 = id, Position = data.Position, Orientation = quaternion,
                    Color = Vector4.One, Scale = Vector3.One, F2 = 0
                };
                i++;

                string basePath = $"/{id}";
                dict[basePath + "/PosX"] = data.Position.X;
                dict[basePath + "/PosY"] = data.Position.Y;
                dict[basePath + "/PosZ"] = data.Position.Z;
                var euler = ToEulerAngles(new Quaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W));
                dict[basePath + "/Pan"] = euler.Y;
                dict[basePath + "/Tilt"] = euler.X;
                dict[basePath + "/Roll"] = euler.Z;
            }

            try
            {
                UpdateGpuBuffer(ref _outputBuffer, _pointArrayForGpu);
                TrackersAsBuffer.Value = _outputBuffer;
                TrackersAsDict.Value = dict;
                SetStatus($"Listening on {localIp}. Tracking {trackerCount} sources.", IStatusProvider.StatusLevel.Success);
            }
            catch (Exception e)
            {
                SetStatus($"GPU buffer update failed: {e.Message}", IStatusProvider.StatusLevel.Error);
            }
        }

        private void StartListening(string localIpStr, string multicastIpStr, int port)
        {
            if (_runListener) return;
            _runListener = true;
            _listenerThread = new Thread(() => ListenLoop(localIpStr, multicastIpStr, port)) { IsBackground = true, Name = "PosiStageInputListener" };
            _listenerThread.Start();
        }

        private void StopListening()
        {
            if (!_runListener) return;
            if (_printToLog) Log.Debug("Stopping PosiStageNet listener.", this);
            _runListener = false;
            _udpClient?.Close();
            _listenerThread?.Join(500); // Increased timeout for robustness
        }

        private void ListenLoop(string localIpStr, string multicastIpStr, int port)
        {
            try
            {
                if (!IsValidMulticastAddress(multicastIpStr))
                {
                    SetStatus($"Invalid multicast IP '{multicastIpStr}'. Must be in 224.0.0.0 - 239.255.255.255.", IStatusProvider.StatusLevel.Error);
                    return;
                }

                var localIp = IPAddress.TryParse(localIpStr, out var parsedIp) ? parsedIp : IPAddress.Any;
                var multicastIp = IPAddress.Parse(multicastIpStr);

                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(localIp, port));
                _udpClient.JoinMulticastGroup(multicastIp, localIp);

                if (_printToLog) Log.Debug($"PSN Input: Bound to {localIp}:{port} and joined group {multicastIp}.", this);

                var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                while (_runListener)
                {
                    try
                    {
                        var data = _udpClient.Receive(ref remoteEp);
                        ParsePsnDataPacket(data);
                        TrackersAsBuffer.DirtyFlag.Invalidate();
                    }
                    catch (SocketException ex)
                    {
                        if (!_runListener) break;
                        if (_printToLog) Log.Warning($"Socket exception: {ex.Message}", this);
                    }
                }
            }
            catch (Exception e)
            {
                SetStatus($"Failed to listen: {e.Message}", IStatusProvider.StatusLevel.Error);
            }
            finally
            {
                _udpClient?.Dispose();
                _runListener = false;
            }
        }

        private void ParsePsnDataPacket(byte[] data)
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            if (reader.BaseStream.Length < 4) return;
            uint rootHeader = reader.ReadUInt32();
            if ((rootHeader & 0xFFFF) != 0x6755) return;

            while (reader.BaseStream.Position < reader.BaseStream.Length - 4)
            {
                uint header = reader.ReadUInt32();
                ushort id = (ushort)(header & 0xFFFF);
                ushort len = (ushort)((header >> 16) & 0x7FFF);
                long nextChunkPos = reader.BaseStream.Position + len;
                if (nextChunkPos > reader.BaseStream.Length) break;

                if (id == 0x0001) // Tracker List
                {
                    while (reader.BaseStream.Position < nextChunkPos - 4)
                    {
                        uint trackerHeader = reader.ReadUInt32();
                        ushort trackerId = (ushort)(trackerHeader & 0xFFFF);
                        ushort trackerLen = (ushort)((trackerHeader >> 16) & 0x7FFF);
                        long trackerEndPos = reader.BaseStream.Position + trackerLen;
                        if (trackerEndPos > nextChunkPos) break;

                        var newPos = Vector3.Zero;
                        var newOri = Vector3.Zero;
                        while (reader.BaseStream.Position < trackerEndPos - 4)
                        {
                            uint fieldHeader = reader.ReadUInt32();
                            ushort fieldId = (ushort)(fieldHeader & 0xFFFF);
                            ushort fieldLen = (ushort)((fieldHeader >> 16) & 0x7FFF);
                            
                            if (reader.BaseStream.Position + fieldLen > trackerEndPos) 
                                break;

                            switch (fieldId)
                            {
                                case 0x0000:
                                    newPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    break;
                                case 0x0002:
                                    newOri = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    break;
                                default:
                                    reader.BaseStream.Position += fieldLen;
                                    break;
                            } 
                        }
                        _trackedValues[trackerId] = new TrackerData(newPos, newOri);
                        reader.BaseStream.Position = trackerEndPos;
                    }
                }
                reader.BaseStream.Position = nextChunkPos;
            }
        }

        private static void UpdateGpuBuffer(ref BufferWithViews? buffer, PsnPoint[] array)
        {
            int count = array.Length;
            if (count != (buffer?.Buffer.Description.SizeInBytes / PsnPoint.Stride ?? 0))
            {
                buffer?.Dispose();
                buffer = new BufferWithViews();
                ResourceManager.SetupStructuredBuffer(array, PsnPoint.Stride * count, PsnPoint.Stride, ref buffer.Buffer);
                ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
                ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
            }
            else if(buffer != null)
            {
                
                ResourceManager.Device.ImmediateContext.UpdateSubresource(array, buffer.Buffer);
            }
        }

        private static Vector4 ToQuaternion(Vector3 axisAngle)
        {
            float angle = axisAngle.Length();
            if (angle < 0.0001f) return new Vector4(0, 0, 0, 1);
            Vector3 axis = Vector3.Normalize(axisAngle);
            float halfAngle = angle / 2.0f;
            float s = MathF.Sin(halfAngle);
            return new Vector4(axis.X * s, axis.Y * s, axis.Z * s, MathF.Cos(halfAngle));
        }

        private static Vector3 ToEulerAngles(Quaternion q)
        {
            Vector3 angles;
            const float radToDeg = 180.0f / MathF.PI;
            var sinp = 2 * (q.W * q.X + q.Y * q.Z);
            angles.X = (MathF.Abs(sinp) >= 1) ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);
            var sinyCosp = 2 * (q.W * q.Y - q.Z * q.X);
            var cosyCosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            angles.Y = MathF.Atan2(sinyCosp, cosyCosp);
            var sinrCosp = 2 * (q.W * q.Z + q.X * q.Y);
            var cosrCosp = 1 - 2 * (q.Z * q.Z + q.X * q.X);
            angles.Z = MathF.Atan2(sinrCosp, cosrCosp);
            return angles * radToDeg;
        }

        private static bool IsValidMulticastAddress(string ipStr)
        {
            if (!IPAddress.TryParse(ipStr, out var ip)) return false;
            var bytes = ip.GetAddressBytes();
            return bytes[0] >= 224 && bytes[0] <= 239;
        }
        
        protected override void Dispose(bool isDisposing)
        {
            if (!isDisposing)
                return;

            StopListening(); 
            _outputBuffer?.Dispose();
        }
        
        #region ICustomDropdownHolder implementation
        string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value ?? string.Empty : string.Empty;
        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id) => id == LocalIpAddress.Id 
                                                                                     ? GetLocalIPv4Addresses() 
                                                                                     : [];

        void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
        {
            if (!string.IsNullOrEmpty(s) && i && id == LocalIpAddress.Id) 
                LocalIpAddress.SetTypedInputValue(s.Split(' ')[0]);
        }

        private static IEnumerable<string> GetLocalIPv4Addresses()
        {
            yield return "0.0.0.0"; // Any
            
            if (!NetworkInterface.GetIsNetworkAvailable()) 
                yield break;
            
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        yield return ipInfo.Address.ToString();
                }
            }
        }
        #endregion

        #region IStatusProvider
        private string? _statusMessage = "Not listening.";
        private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;

        private void SetStatus(string m, IStatusProvider.StatusLevel l)
        {
            _statusMessage = m; _statusLevel = l;
        }
        public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
        public string? GetStatusMessage() => _statusMessage;
        #endregion

        [Input(Guid = "0944714D-693D-4251-93A6-E22A2DB64F20")]
        public readonly InputSlot<bool> Listen = new();
        
        [Input(Guid = "9E23335A-D63A-4286-930E-C63E86D0E6F0")]
        public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0");
        
        [Input(Guid = "46C2BF8B-3E0C-4856-AA4A-32943A4B0223")]
        public readonly InputSlot<string> MulticastIpAddress = new("236.10.10.10");
        
        [Input(Guid = "2EBE418D-407E-46D8-B274-13B41C52ACCF")]
        public readonly InputSlot<int> Port = new(56565);
        
        [Input(Guid = "5E725916-4143-4759-8651-E12185C658D3")]
        public readonly InputSlot<bool> PrintToLog = new();
        
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct PsnPoint
    {
        public Vector3 Position;
        public float F1; // Used for ID
        public Vector4 Orientation; // Quaternion
        public Vector4 Color;
        public Vector3 Scale;
        public float F2;

        internal const int Stride = 64;
    }
}