#nullable enable
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SharpDX;
using SharpDX.Direct3D11;
using Exception = System.Exception;

namespace Lib.io.posistage
{
    [Guid("F1B2A3C4-D5E6-4F7A-8B9C-0D1E2F3A4B5C")]
    internal sealed class PosiStageOutput : Instance<PosiStageOutput>, IStatusProvider, ICustomDropdownHolder
    {
        private readonly Stopwatch _infoPacketStopwatch = new();
        private readonly Stopwatch _stopwatch = new();

        [Output(Guid = "AABBCCDD-EEFF-4012-3456-7890ABCDEF12")]
        public readonly Slot<Command> Command = new();

        [Input(Guid = "7AB8F2A6-4874-4235-85A5-D0E1F30C0446")]
        public readonly InputSlot<bool> Connect = new();

        [Output(Guid = "07B57F3A-8993-4B9F-8349-D0A4762E4447")]
        public readonly Slot<bool> IsConnected = new();

        [Input(Guid = "9E23335A-D63A-4286-930E-C63E86D0E6F0")]
        public readonly InputSlot<string> LocalIpAddress = new("127.0.0.1");

        [Input(Guid = "B2B8C4F1-6D0E-4B3A-9C8E-7F1A0D9E6B5B")]
        public readonly MultiInputSlot<string> Names = new();

        [Input(Guid = "5E725916-4143-4759-8651-E12185C658D3")]
        public readonly InputSlot<bool> PrintToLog = new();

        [Input(Guid = "B16A0356-EF4A-413A-A656-7497127E31D4")]
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "D7AC22C0-A31E-41F6-B29D-D40956E6688B")]
        public readonly InputSlot<bool> SendTrigger = new();

        [Input(Guid = "4A9E2D3B-8C6F-4B1D-8D7E-9F3A5B2C1D0E")]
        public readonly InputSlot<string> ServerName = new("T3 PSN Output");

        [Input(Guid = "24B5D450-4E83-49DB-88B1-7D688E64585D")]
        public readonly InputSlot<string> TargetIpAddress = new("236.10.10.10");

        [Input(Guid = "36C2BF8B-3E0C-4856-AA4A-32943A4B0223")]
        public readonly InputSlot<int> TargetPort = new(56565);

        [Input(Guid = "C8C4D0F7-C06E-4B1A-9D6C-6F5E4D3C2B1B")]
        public readonly InputSlot<BufferWithViews> TrackerData = new();

        private PsnPoint[]? _cpuSidePoints;
        private byte _frameIdCounter;
        private bool _lastConnectState;
        private string _lastLocalIp = string.Empty;
        private IPEndPoint? _multicastEndPoint;
        private bool _printToLog;

        private UdpClient? _udpClient;

        public PosiStageOutput()
        {
            Command.UpdateAction = Update;
            IsConnected.UpdateAction = Update;
        }

        private void Update(EvaluationContext context)
        {
            _printToLog = PrintToLog.GetValue(context);
            var shouldConnect = Connect.GetValue(context);
            var localIp = LocalIpAddress.GetValue(context);
            var targetIp = TargetIpAddress.GetValue(context);
            var targetPort = TargetPort.GetValue(context);

            var settingsChanged = shouldConnect != _lastConnectState || localIp != _lastLocalIp;
            if (settingsChanged)
            {
                CloseSocket();
                if (shouldConnect) OpenSocket(localIp);
                _lastConnectState = shouldConnect;
                _lastLocalIp = localIp;
            }

            IsConnected.Value = _udpClient != null;

            var bufferWithViews = TrackerData.GetValue(context);
            var buffer = bufferWithViews?.Buffer;
            if (buffer == null)
            {
                SetStatus("Input buffer is not connected.", IStatusProvider.StatusLevel.Warning);
                return;
            }

            int pointCount = buffer.Description.SizeInBytes / PsnPoint.Stride;
            if (pointCount == 0) return;

            if (!ReadBufferData(buffer, ref _cpuSidePoints, pointCount))
                return;

            var manualTrigger = SendTrigger.GetValue(context);

            if (IsConnected.Value && (manualTrigger || SendOnChange.GetValue(context)) && _cpuSidePoints != null)
            {
                if (manualTrigger)
                    SendTrigger.SetTypedInputValue(false);

                if (!IPAddress.TryParse(targetIp, out var targetIpAddr))
                {
                    SetStatus($"Invalid Target IP '{targetIp}'", IStatusProvider.StatusLevel.Error);
                    return;
                }

                _multicastEndPoint = new IPEndPoint(targetIpAddr, targetPort);

                var frameId = _frameIdCounter++;
                var packetBytes = BuildPsnDataPacket(_cpuSidePoints, pointCount, frameId);
                try
                {
                    _udpClient!.Send(packetBytes, packetBytes.Length, _multicastEndPoint);
                    SetStatus($"Sent 1 packet with {pointCount} trackers for frame {frameId}.", IStatusProvider.StatusLevel.Success);
                    if (_printToLog) Log.Debug($"Sent 1 PSN_DATA packet with {pointCount} trackers for frame {frameId}.", this);
                }
                catch (Exception e)
                {
                    SetStatus($"UDP send error: {e.Message}", IStatusProvider.StatusLevel.Warning);
                    return;
                }

                if (!(_infoPacketStopwatch.Elapsed.TotalSeconds > 1.0))
                    return;

                var names = Names.GetCollectedTypedInputs();
                var serverName = ServerName.GetValue(context);
                var infoPacket = BuildPsnInfoPacket(pointCount, names, context, serverName);
                try
                {
                    _udpClient!.Send(infoPacket, infoPacket.Length, _multicastEndPoint);
                }
                catch
                {
                    /* ignored */
                }

                _infoPacketStopwatch.Restart();
            }
        }

        private byte[] BuildPsnDataPacket(PsnPoint[] points, int pointCount, byte frameId)
        {
            using var packetStream = new MemoryStream();
            using var writer = new BinaryWriter(packetStream, Encoding.ASCII, true);

            // Packet Header Chunk
            WriteChunkHeader(writer, 0x0000, 12, false);
            writer.Write((ulong)_stopwatch.Elapsed.TotalMilliseconds * 1000);
            writer.Write((byte)2);
            writer.Write((byte)3); // Version 2.03
            writer.Write(frameId);
            writer.Write((byte)1); // Frame Packet Count: 1

            // Tracker List Chunk (containing all trackers)
            using var trackerListStream = new MemoryStream();
            using var trackerListWriter = new BinaryWriter(trackerListStream);

            for (int i = 0; i < pointCount; i++)
            {
                var trackerId = (ushort)i; // Start at 0
                var point = points[i];
                var quat = new Quaternion(point.Orientation.X, point.Orientation.Y, point.Orientation.Z, point.Orientation.W);

                using var trackerDataStream = new MemoryStream();
                using var trackerDataWriter = new BinaryWriter(trackerDataStream);

                // Position Sub-Chunk (with Z-axis flip)
                WriteChunkHeader(trackerDataWriter, 0x0000, 12, false);
                trackerDataWriter.Write(point.Position.X);
                trackerDataWriter.Write(point.Position.Y);
                trackerDataWriter.Write(-point.Position.Z);

                // Orientation Sub-Chunk (with Z-axis flip)
                var axisAngle = ToAxisAngleVector(quat);
                WriteChunkHeader(trackerDataWriter, 0x0002, 12, false);
                trackerDataWriter.Write(axisAngle.X);
                trackerDataWriter.Write(axisAngle.Y);
                trackerDataWriter.Write(-axisAngle.Z);

                // Write the tracker data into the tracker list
                WriteChunkHeader(trackerListWriter, trackerId, (ushort)trackerDataStream.Length, true);
                trackerDataStream.WriteTo(trackerListWriter.BaseStream);
            }

            // Write the tracker list into the main packet stream
            WriteChunkHeader(writer, 0x0001, (ushort)trackerListStream.Length, true);
            trackerListStream.WriteTo(writer.BaseStream);

            // Wrap everything in the final root packet chunk
            var finalPacket = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalPacket);
            WriteChunkHeader(finalWriter, 0x6755, (ushort)packetStream.Length, true);
            packetStream.WriteTo(finalWriter.BaseStream);

            return finalPacket.ToArray();
        }

        private byte[] BuildPsnInfoPacket(int pointCount, List<Slot<string>> names, EvaluationContext context, string serverName)
        {
            using var packetStream = new MemoryStream();
            using var writer = new BinaryWriter(packetStream, Encoding.ASCII, true);

            WriteChunkHeader(writer, 0x0000, 12, false);
            writer.Write((ulong)_stopwatch.Elapsed.TotalMilliseconds * 1000);
            writer.Write((byte)2);
            writer.Write((byte)3);
            writer.Write((byte)0);
            writer.Write((byte)1);

            var serverNameBytes = Encoding.ASCII.GetBytes(serverName);
            WriteChunkHeader(writer, 0x0001, (ushort)serverNameBytes.Length, false);
            writer.Write(serverNameBytes);

            using var trackerListStream = new MemoryStream();
            using var trackerListWriter = new BinaryWriter(trackerListStream);
            for (int i = 0; i < pointCount; i++)
            {
                var trackerId = (ushort)i; // Start at 0
                var trackerName = i < names.Count ? names[i].GetValue(context) : $"Tracker_{trackerId}";
                var trackerNameBytes = Encoding.ASCII.GetBytes(trackerName);

                using var trackerNameStream = new MemoryStream();
                using var trackerNameWriter = new BinaryWriter(trackerNameStream);
                WriteChunkHeader(trackerNameWriter, 0x0000, (ushort)trackerNameBytes.Length, false);
                trackerNameWriter.Write(trackerNameBytes);

                WriteChunkHeader(trackerListWriter, trackerId, (ushort)trackerNameStream.Length, true);
                trackerNameStream.WriteTo(trackerListWriter.BaseStream);
            }

            WriteChunkHeader(writer, 0x0002, (ushort)trackerListStream.Length, true);
            trackerListStream.WriteTo(writer.BaseStream);

            var finalPacket = new MemoryStream();
            using var finalWriter = new BinaryWriter(finalPacket);
            WriteChunkHeader(finalWriter, 0x6756, (ushort)packetStream.Length, true);
            packetStream.WriteTo(finalWriter.BaseStream);

            return finalPacket.ToArray();
        }

        private bool ReadBufferData(Buffer buffer, ref PsnPoint[]? data, int pointCount)
        {
            if (data == null || data.Length != pointCount)
                data = new PsnPoint[pointCount];

            var device = ResourceManager.Device;
            var immediateContext = device.ImmediateContext;
            var description = buffer.Description;
            description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None;
            description.CpuAccessFlags = CpuAccessFlags.Read;
            description.OptionFlags = ResourceOptionFlags.None;

            try
            {
                using (var stagingBuffer = new Buffer(device, description))
                {
                    immediateContext.CopyResource(buffer, stagingBuffer);
                    var dataBox = immediateContext.MapSubresource(stagingBuffer, 0, MapMode.Read, MapFlags.None);
                    Utilities.Read(dataBox.DataPointer, data, 0, pointCount);
                    immediateContext.UnmapSubresource(stagingBuffer, 0);
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to read buffer from GPU: {e.Message}", this);
                return false;
            }
        }

        private void OpenSocket(string localIpStr)
        {
            if (!IPAddress.TryParse(localIpStr, out var localIp))
            {
                SetStatus($"Invalid Local IP '{localIpStr}'.", IStatusProvider.StatusLevel.Error);
                return;
            }

            try
            {
                _udpClient = new UdpClient(new IPEndPoint(localIp, 0));
                if (_printToLog) Log.Debug($"PSN Output: Socket bound to local IP {localIp}", this);
                _stopwatch.Restart();
                _infoPacketStopwatch.Restart();
                SetStatus($"Socket ready on {localIp}", IStatusProvider.StatusLevel.Success);
            }
            catch (Exception e)
            {
                SetStatus($"Failed to open socket on {localIp}: {e.Message}", IStatusProvider.StatusLevel.Error);
            }
        }

        private void CloseSocket()
        {
            _udpClient?.Dispose();
            _udpClient = null;
            _stopwatch.Stop();
            if (_lastConnectState) SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
        }

        private static void WriteChunkHeader(BinaryWriter writer, ushort id, ushort length, bool hasSubChunks)
        {
            uint header = id;
            header |= (uint)length << 16;
            if (hasSubChunks) header |= 1u << 31;
            writer.Write(header);
        }

        private static Vector3 ToAxisAngleVector(Quaternion q)
        {
            if (q.W > 1.0f) q = Quaternion.Normalize(q);
            float angle = 2.0f * MathF.Acos(q.W);
            float s = MathF.Sqrt(1.0f - q.W * q.W);
            if (s < 0.001f) return Vector3.Zero;
            return new Vector3(q.X / s, q.Y / s, q.Z / s) * angle;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (!isDisposing)
                return;

            CloseSocket();
        }

        #region ICustomDropdownHolder
        string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
        {
            return id == LocalIpAddress.Id ? GetLocalIPv4Addresses() : Empty<string>();
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
                {
                    if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        yield return ipInfo.Address.ToString();
                }
            }
        }
        #endregion

        #region IStatusProvider
        private string? _statusMessage = "Not connected.";
        private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;

        private void SetStatus(string m, IStatusProvider.StatusLevel l)
        {
            _statusMessage = m;
            _statusLevel = l;
        }

        public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
        public string? GetStatusMessage() => _statusMessage;
        #endregion
    }
}