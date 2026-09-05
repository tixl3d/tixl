
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using T3.Core.Animation;

namespace Lib.io.ptz
{
    [Guid("7c90d60d-0d12-736d-ad90-1d2e3f4d5d6d")]
    public class ViscaCamera : Instance<ViscaCamera>, ICustomDropdownHolder
    {
        [Output(Guid = "A1B2C3D4-E5F6-4789-0123-456789ABCDEF")]
        public readonly Slot<Texture2D> TextureOutput = new();

        [Output(Guid = "8d01e71e-1e23-847e-be01-2e3f4e5e6e7e")]
        public readonly Slot<bool> IsConnected = new();

        [Output(Guid = "E1F2A3B4-C5D6-E7F8-9012-3456789ABCDE", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Vector3> CurrentPtz = new();

        public ViscaCamera()
        {
            IsConnected.UpdateAction += Update;
            TextureOutput.UpdateAction += Update;
            CurrentPtz.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            // Pass-through texture
            TextureOutput.Value = TextureInput.GetValue(context);

            // --- Network Interface Management ---
            var localIpString = LocalIpAddress.GetValue(context);
            if (string.IsNullOrEmpty(localIpString))
            {
                if (Playback.RunTimeInSecs - _lastNetworkRefreshTime > 5.0)
                {
                    _lastNetworkRefreshTime = Playback.RunTimeInSecs;
                    _networkInterfaces = GetNetworkInterfaces();
                }
            }

            var address = Address.GetValue(context);
            var port = Port.GetValue(context);
            var shouldConnect = Connect.GetValue(context);
            var printToLog = PrintToLog.GetValue(context);

            var pan = Pan.GetValue(context);
            var tilt = Tilt.GetValue(context);
            var zoom = Zoom.GetValue(context);
            var doMove = Move.GetValue(context);
            
            var panRange = PanRange.GetValue(context);
            var tiltRange = TiltRange.GetValue(context);
            
            // Update ranges for the receive loop to use
            _currentPanRange = panRange > 0 ? panRange : 1;
            _currentTiltRange = tiltRange > 0 ? tiltRange : 1;

            if (shouldConnect != _isConnected)
            {
                if (shouldConnect)
                {
                    ConnectToCamera(address, port, localIpString, printToLog);
                }
                else
                {
                    Disconnect(printToLog);
                }
            }
            else if (shouldConnect && !_isConnected)
            {
                if (Playback.RunTimeInSecs - _lastRetryTime > 2.0)
                {
                    _lastRetryTime = Playback.RunTimeInSecs;
                    ConnectToCamera(address, port, localIpString, printToLog);
                }
            }

            if (_isConnected)
            {
                if (doMove)
                {
                    // Throttling to avoid flooding the camera
                    if ((DateTime.Now - _lastSendTime).TotalMilliseconds > 50) // Max 20Hz
                    {
                        _lastSendTime = DateTime.Now;
                        // Fire and forget, but on a thread pool to avoid blocking Update
                        _ = SendAbsoluteMoveAsync(pan, tilt, zoom, panRange, tiltRange, printToLog);
                    }
                }

                // Polling for position
                if (Playback.RunTimeInSecs - _lastPtzPollTime > 0.1) // 10Hz polling
                {
                    _lastPtzPollTime = Playback.RunTimeInSecs;
                    _ = PollPtzAsync(printToLog);
                }
            }

            IsConnected.Value = _isConnected;
            CurrentPtz.Value = _currentPtz;
        }

        private void ConnectToCamera(string address, int port, string localIpStr, bool log)
        {
            try
            {
                if (string.IsNullOrEmpty(address)) return;
                
                // Close existing if any
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _udpClient?.Dispose();

                // Bind to specific local IP if provided
                IPEndPoint localEndPoint = null;
                if (!string.IsNullOrEmpty(localIpStr) && IPAddress.TryParse(localIpStr, out var localIp))
                {
                    localEndPoint = new IPEndPoint(localIp, 0);
                }

                _udpClient = localEndPoint != null ? new UdpClient(localEndPoint) : new UdpClient();
                _udpClient.Connect(address, port);
                _sequenceNumber = 1;
                
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Start Receive Loop
                Task.Run(() => ReceiveLoop(_cancellationTokenSource.Token, log));
                
                // Reset sequence number
                Task.Run(() => ResetSequenceNumber(log));
                
                _isConnected = true;
                if (log) Log.Debug($"ViscaCamera: Connected to {address}:{port} via {localIpStr ?? "Default Interface"}", this);
            }
            catch (Exception e)
            {
                if (log) Log.Warning($"ViscaCamera: Connection failed: {e.Message}", this);
                _isConnected = false;
            }
        }

        private void Disconnect(bool log)
        {
            try 
            {
                _cancellationTokenSource?.Cancel();
                _udpClient?.Close();
                _udpClient?.Dispose();
            }
            catch
            {
                // ignored
            }

            _udpClient = null;
            _isConnected = false;
            if (log) Log.Debug("ViscaCamera: Disconnected", this);
        }

        private async Task ReceiveLoop(CancellationToken token, bool log)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_udpClient == null) break;
                    var result = await _udpClient.ReceiveAsync();
                    ProcessPacket(result.Buffer);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (log && !token.IsCancellationRequested) Log.Warning($"ViscaCamera: Receive error: {e.Message}", this);
                    // Wait a bit before retrying to avoid tight loop on error
                    await Task.Delay(100, token);
                }
            }
        }

        private void ProcessPacket(byte[] data)
        {
            // VISCA over IP packet structure:
            // 00-01: Payload Type (0x0111 for Reply)
            // 02-03: Payload Length
            // 04-07: Sequence Number
            // 08-..: Payload (VISCA response)

            if (data.Length < 9) return; // Header (8) + Min Payload (1)

            // Check for VISCA Reply (0x0111)
            if (data[0] != 0x01 || data[1] != 0x11) return;

            int payloadIndex = 8;
            
            // Check for Pan/Tilt Inquiry Response
            // Response: Y0 50 0w 0w 0w 0w 0z 0z 0z 0z FF (11 bytes payload)
            if (data.Length >= payloadIndex + 11)
            {
                if (data[payloadIndex + 1] == 0x50 && data[payloadIndex + 10] == 0xFF)
                {
                    // Let's check payload length from header
                    int payloadLen = (data[2] << 8) | data[3];
                    
                    if (payloadLen == 11)
                    {
                        // Parse Pan/Tilt with masking for safety
                        int panRaw = ((data[payloadIndex + 2] & 0x0F) << 12) | 
                                     ((data[payloadIndex + 3] & 0x0F) << 8) | 
                                     ((data[payloadIndex + 4] & 0x0F) << 4) | 
                                     (data[payloadIndex + 5] & 0x0F);
                                     
                        int tiltRaw = ((data[payloadIndex + 6] & 0x0F) << 12) | 
                                      ((data[payloadIndex + 7] & 0x0F) << 8) | 
                                      ((data[payloadIndex + 8] & 0x0F) << 4) | 
                                      (data[payloadIndex + 9] & 0x0F);
                        
                        short panSigned = (short)panRaw;
                        short tiltSigned = (short)tiltRaw;
                        
                        // Normalize to -1..1 based on Range
                        float pan = (float)panSigned / _currentPanRange;
                        float tilt = (float)tiltSigned / _currentTiltRange;
                        
                        _currentPtz.X = pan;
                        _currentPtz.Y = tilt;
                    }
                }
            }
            
            // Check for Zoom Inquiry Response
            // Response: Y0 50 0p 0q 0r 0s FF (7 bytes payload)
            if (data.Length >= payloadIndex + 7)
            {
                int payloadLen = (data[2] << 8) | data[3];
                if (payloadLen == 7 && data[payloadIndex + 1] == 0x50 && data[payloadIndex + 6] == 0xFF)
                {
                    int zoomRaw = ((data[payloadIndex + 2] & 0x0F) << 12) | 
                                  ((data[payloadIndex + 3] & 0x0F) << 8) | 
                                  ((data[payloadIndex + 4] & 0x0F) << 4) | 
                                  (data[payloadIndex + 5] & 0x0F);
                    
                    // Normalize Zoom (0x0000 to 0x4000 usually)
                    float zoom = (float)zoomRaw / 0x4000;
                    _currentPtz.Z = zoom;
                }
            }
        }

        private async Task ResetSequenceNumber(bool log)
        {
            byte[] packet = new byte[] {
                0x02, 0x00, 0x00, 0x01, 
                0x00, 0x00, 0x00, 0x01, 
                0x01 
            };
            await SendPacketAsync(packet, log);
            _sequenceNumber = 1;
        }

        private async Task PollPtzAsync(bool log)
        {
            // Pan-Tilt Pos Inquiry: 81 09 06 12 FF
            byte[] ptInq = new byte[] { 0x81, 0x09, 0x06, 0x12, 0xFF };
            await SendViscaCommandAsync(ptInq, 0x0110, log); // 0x0110 = VISCA Inquiry

            // Zoom Pos Inquiry: 81 09 04 47 FF
            byte[] zoomInq = new byte[] { 0x81, 0x09, 0x04, 0x47, 0xFF };
            await SendViscaCommandAsync(zoomInq, 0x0110, log); // 0x0110 = VISCA Inquiry
        }

        private async Task SendAbsoluteMoveAsync(float pan, float tilt, float zoom, int panRange, int tiltRange, bool log)
        {
            // Map -1..1 to Range
            int p = (int)(Math.Clamp(pan, -1f, 1f) * panRange);
            int t = (int)(Math.Clamp(tilt, -1f, 1f) * tiltRange);
            
            // Zoom is 0..1 -> 0x0000 to 0x4000 (standard Sony range)
            int z = (int)(Math.Clamp(zoom, 0f, 1f) * 0x4000); 

            byte speed = 0x18;

            byte[] viscaCmd = new byte[15];
            viscaCmd[0] = 0x81;
            viscaCmd[1] = 0x01;
            viscaCmd[2] = 0x06;
            viscaCmd[3] = 0x02; // Absolute Move
            viscaCmd[4] = speed; // Pan Speed
            viscaCmd[5] = speed; // Tilt Speed
            
            Set4ByteVal(viscaCmd, 6, p);
            Set4ByteVal(viscaCmd, 10, t);
            
            viscaCmd[14] = 0xFF;

            await SendViscaCommandAsync(viscaCmd, 0x0100, log); // 0x0100 = VISCA Command

            // Zoom Absolute: 81 01 04 47 0p 0q 0r 0s FF
            byte[] zoomCmd = new byte[9];
            zoomCmd[0] = 0x81;
            zoomCmd[1] = 0x01;
            zoomCmd[2] = 0x04;
            zoomCmd[3] = 0x47;
            Set4ByteVal(zoomCmd, 4, z);
            zoomCmd[8] = 0xFF;
            
            await SendViscaCommandAsync(zoomCmd, 0x0100, log); // 0x0100 = VISCA Command
        }

        private void Set4ByteVal(byte[] buffer, int offset, int value)
        {
            short v = (short)value;
            buffer[offset]   = (byte)((v >> 12) & 0x0F);
            buffer[offset+1] = (byte)((v >> 8) & 0x0F);
            buffer[offset+2] = (byte)((v >> 4) & 0x0F);
            buffer[offset+3] = (byte)((v) & 0x0F);
        }

        private async Task SendViscaCommandAsync(byte[] cmd, ushort payloadType, bool log)
        {
            byte[] packet = new byte[8 + cmd.Length];
            packet[0] = (byte)((payloadType >> 8) & 0xFF); // Payload Type High
            packet[1] = (byte)(payloadType & 0xFF);        // Payload Type Low
            packet[2] = (byte)((cmd.Length >> 8) & 0xFF); // Length High
            packet[3] = (byte)(cmd.Length & 0xFF);        // Length Low
            
            uint seq = _sequenceNumber++;
            packet[4] = (byte)((seq >> 24) & 0xFF);
            packet[5] = (byte)((seq >> 16) & 0xFF);
            packet[6] = (byte)((seq >> 8) & 0xFF);
            packet[7] = (byte)(seq & 0xFF);
            
            Array.Copy(cmd, 0, packet, 8, cmd.Length);
            
            await SendPacketAsync(packet, log);
        }

        private async Task SendPacketAsync(byte[] packet, bool log)
        {
            if (_udpClient == null) return;
            try
            {
                await _udpClient.SendAsync(packet, packet.Length);
            }
            catch (ObjectDisposedException) 
            {
                // Client was closed, ignore
            }
            catch (Exception e)
            {
                if (log) Log.Warning($"ViscaCamera: Send failed: {e.Message}", this);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (!isDisposing) return;
            Disconnect(false);
        }

        #region Network Interface Logic
        private static List<NetworkAdapterInfo> _networkInterfaces = new();

        private static List<NetworkAdapterInfo> GetNetworkInterfaces()
        {
            var list = new List<NetworkAdapterInfo>();
            list.Add(new NetworkAdapterInfo(IPAddress.Any, IPAddress.Any, "Any"));
            list.Add(new NetworkAdapterInfo(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), "Localhost"));
            
            try
            {
                list.AddRange(from ni in NetworkInterface.GetAllNetworkInterfaces()
                              where ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                              from ip in ni.GetIPProperties().UnicastAddresses
                              where ip.Address.AddressFamily == AddressFamily.InterNetwork
                              select new NetworkAdapterInfo(ip.Address, ip.IPv4Mask, ni.Name));
            }
            catch (Exception e)
            {
                Log.Warning("Could not enumerate network interfaces: " + e.Message);
            }
            return list;
        }

        private sealed record NetworkAdapterInfo(IPAddress IpAddress, IPAddress SubnetMask, string Name)
        {
            public string DisplayName => $"{Name} ({IpAddress})";
        }
        #endregion

        #region ICustomDropdownHolder
        string ICustomDropdownHolder.GetValueForInput(Guid inputId)
        {
            if (inputId == LocalIpAddress.Id) return LocalIpAddress.Value ?? string.Empty;
            return string.Empty;
        }

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
        {
            if (inputId == LocalIpAddress.Id)
            {
                _networkInterfaces = GetNetworkInterfaces();
                foreach (var adapter in _networkInterfaces) yield return adapter.DisplayName;
            }
        }

        void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
        {
            if (string.IsNullOrEmpty(selected) || !isAListItem) return;
            if (inputId == LocalIpAddress.Id)
            {
                var foundAdapter = _networkInterfaces.FirstOrDefault(i => i.DisplayName == selected);
                if (foundAdapter != null) LocalIpAddress.SetTypedInputValue(foundAdapter.IpAddress.ToString());
            }
        }
        #endregion

        private UdpClient _udpClient;
        private bool _isConnected;
        private uint _sequenceNumber;
        private DateTime _lastSendTime;
        private double _lastNetworkRefreshTime;
        private double _lastRetryTime;
        private double _lastPtzPollTime;
        private CancellationTokenSource _cancellationTokenSource;
        private Vector3 _currentPtz;
        private int _currentPanRange = 1;
        private int _currentTiltRange = 1;

        [Input(Guid = "B2C3D4E5-F6A7-4890-1234-567890ABCDEF")]
        public readonly InputSlot<Texture2D> TextureInput = new();

        [Input(Guid = "34185692-2982-4895-8963-125478963214")]
        public readonly InputSlot<string> LocalIpAddress = new();

        [Input(Guid = "9a23a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<string> Address = new();

        [Input(Guid = "a123a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<int> Port = new();

        [Input(Guid = "b223a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<bool> Connect = new();

        [Input(Guid = "c323a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<float> Pan = new();

        [Input(Guid = "d423a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<float> Tilt = new();

        [Input(Guid = "e523a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<float> Zoom = new();

        [Input(Guid = "f623a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<bool> Move = new();
        
        [Input(Guid = "0723a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<int> PanRange = new();

        [Input(Guid = "1823a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<int> TiltRange = new();

        [Input(Guid = "2923a93a-3a45-069a-da23-4a5a6a7a8a9a")]
        public readonly InputSlot<bool> PrintToLog = new();
    }
}