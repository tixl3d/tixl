#nullable enable
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using T3.Core.Utils;

namespace Lib.io.tcp
{
    [Guid("A2B3C4D5-E6F7-4890-1234-567890ABCDEF")]
    internal sealed class TcpClient : Instance<TcpClient>
                                    , IStatusProvider, ICustomDropdownHolder, IDisposable
    {
        private readonly List<string> _messageHistory = new();
        private readonly ConcurrentQueue<string> _receivedQueue = new();
        private readonly object _socketLock = new();

        [Input(Guid = "C2D3E4F5-A6B7-4890-1234-567890ABCDEF")]
        public readonly InputSlot<bool> Connect = new();

        [Input(Guid = "F1E2D3C4-B5A6-4789-0123-456789ABCDEF")]
        public readonly InputSlot<string> Host = new("localhost");

        [Output(Guid = "3B4C5D6E-7F8A-4901-2345-67890ABCDEF1")]
        public readonly Slot<bool> IsConnected = new();

        [Input(Guid = "D4E5F6A7-B8C9-4012-3456-7890ABCDEF12")]
        public readonly InputSlot<int> ListLength = new(10);

        [Input(Guid = "B5C6D7E8-F9A0-4123-4567-890ABCDEF123")]
        public readonly MultiInputSlot<string> MessageParts = new();

        [Input(Guid = "A3B4C5D6-E7F8-4901-2345-67890ABCDEF1")]
        public readonly InputSlot<int> Port = new(8080);

        [Input(Guid = "3C4D5E6F-7A8B-4901-2345-67890ABCDEF1")]
        public readonly InputSlot<bool> PrintToLog = new();

        [Output(Guid = "D5C4B3A2-E1F0-4987-6543-210FEDCBA987", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<string>> ReceivedLines = new();

        [Output(Guid = "F1E0D9C8-7B6A-4543-210F-EDCBA9876543", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<string> ReceivedString = new();

        [Input(Guid = "F7A8B9C0-D1E2-4345-6789-0ABCDEF12345")]
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "1B2C3D4E-5F6A-4789-0123-456789ABCDEF")]
        public readonly InputSlot<bool> SendTrigger = new();

        [Input(Guid = "E6F7A8B9-C0D1-4234-5678-90ABCDEF1234")]
        public readonly InputSlot<string> Separator = new(" ");

        [Input(Guid = "759368A6-5123-4E6B-9087-123456789ABC")]
        public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)");

        [Output(Guid = "1A2B3C4D-5E6F-4789-A0B1-C2D3E4F5A6B7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<bool> WasTrigger = new();

        private CancellationTokenSource? _cts;
        private bool _disposed;
        private bool _lastConnectState;
        private string? _lastHost;
        private int _lastPort;
        private string? _lastLocalIp;
        private string? _lastSentMessage;
        private bool _printToLog;

        private TcpClientSocket? _socket;

        public TcpClient()
        {
            ReceivedString.UpdateAction = Update;
            ReceivedLines.UpdateAction = Update;
            WasTrigger.UpdateAction = Update;
            IsConnected.UpdateAction = Update;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Task.Run(StopAsync);
            _receivedQueue.Clear();
            _messageHistory.Clear();
        }

        private void Update(EvaluationContext context)
        {
            if (_disposed)
                return;

            _printToLog = PrintToLog.GetValue(context);
            var shouldConnect = Connect.GetValue(context);
            var host = Host.GetValue(context);
            var port = Port.GetValue(context);
            var localIp = LocalIpAddress.GetValue(context);

            var settingsChanged = shouldConnect != _lastConnectState || host != _lastHost || port != _lastPort || localIp != _lastLocalIp;
            if (settingsChanged)
            {
                _ = HandleConnectionChange(shouldConnect, host, port, localIp);
            }

            HandleMessageSending(context);
            HandleReceivedMessages(context);
            UpdateStatusMessage();
        }

        private async Task HandleConnectionChange(bool shouldConnect, string? host, int port, string? localIp)
        {
            await StopAsync();
            _lastConnectState = shouldConnect;
            _lastHost = host;
            _lastPort = port;
            _lastLocalIp = localIp;

            if (shouldConnect && !string.IsNullOrWhiteSpace(host))
            {
                await StartAsync(host, port, localIp);
            }
        }

        private void HandleMessageSending(EvaluationContext context)
        {
            var separator = Separator.GetValue(context) ?? "";
            var messageParts = MessageParts.GetCollectedTypedInputs().Select(p => p.GetValue(context));
            var currentMessage = string.Join(separator, messageParts);
            var hasMessageChanged = currentMessage != _lastSentMessage;
            var manualTrigger = SendTrigger.GetValue(context);
            var sendOnChange = SendOnChange.GetValue(context);
            var shouldSend = manualTrigger || (sendOnChange && hasMessageChanged);

            if (IsConnected.Value && shouldSend && !string.IsNullOrEmpty(currentMessage))
            {
                if (manualTrigger)
                    SendTrigger.SetTypedInputValue(false);

                _ = SendMessageAsync(currentMessage);
                _lastSentMessage = currentMessage;
            }
        }

        private void HandleReceivedMessages(EvaluationContext context)
        {
            var listLength = ListLength.GetValue(context).Clamp(1, 1000);
            var wasTriggered = false;

            while (_receivedQueue.TryDequeue(out var msg))
            {
                ReceivedString.Value = msg;
                _messageHistory.Add(msg);
                wasTriggered = true;
            }

            while (_messageHistory.Count > listLength)
                _messageHistory.RemoveAt(0);

            ReceivedLines.Value = new List<string>(_messageHistory);
            WasTrigger.Value = wasTriggered;
            IsConnected.Value = _socket?.IsConnected ?? false;
        }

        private async Task StartAsync(string host, int port, string? localIp)
        {
            lock (_socketLock)
            {
                _socket?.Dispose();
                _socket = new TcpClientSocket();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
            }

            try
            {
                SetStatus($"Connecting to {host}:{port}...", IStatusProvider.StatusLevel.Notice);
                if (_printToLog)
                {
                    Log.Debug($"TCP Client: Attempting to connect to {host}:{port}...", this);
                }

                await _socket!.ConnectAsync(host, port, localIp);
                SetStatus($"Connected to {host}:{port}", IStatusProvider.StatusLevel.Success);
                if (_printToLog)
                {
                    Log.Debug($"TCP Client: Connected to {host}:{port}", this);
                }

                _ = Task.Run(ReceiveLoop);
            }
            catch (Exception e)
            {
                SetStatus($"Connect failed: {e.Message}", IStatusProvider.StatusLevel.Error);
                if (_printToLog)
                {
                    Log.Error($"TCP Client: Connect failed to {host}:{port}: {e.Message}", this);
                }

                lock (_socketLock)
                {
                    _socket?.Dispose();
                    _socket = null;
                }
            }
            finally
            {
                IsConnected.DirtyFlag.Invalidate();
            }
        }

        private async Task StopAsync()
        {
            TcpClientSocket? socketToDispose = null;
            CancellationTokenSource? ctsToDispose = null;

            lock (_socketLock)
            {
                if (_socket != null)
                {
                    socketToDispose = _socket;
                    _socket = null;
                }

                if (_cts != null)
                {
                    ctsToDispose = _cts;
                    _cts = null;
                }
            }

            try
            {
                ctsToDispose?.Cancel();

                if (_printToLog)
                {
                    Log.Debug($"TCP Client: Stopping connection.", this);
                }

                socketToDispose?.Dispose();
                ctsToDispose?.Dispose();

                SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
                if (_printToLog)
                {
                    Log.Debug("TCP Client: Disconnected.", this);
                }

                IsConnected.DirtyFlag.Invalidate();
            }
            catch (Exception e)
            {
                Log.Warning($"TCP Client: Stop error: {e.Message}", this);
            }

            await Task.Yield();
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    TcpClientSocket? currentSocket;
                    CancellationToken cancellationToken;

                    lock (_socketLock)
                    {
                        currentSocket = _socket;
                        cancellationToken = _cts?.Token ?? CancellationToken.None;
                        if (currentSocket == null || !currentSocket.IsConnected)
                            break;
                    }

                    var bytesRead = await currentSocket.ReceiveAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                    {
                        if (_printToLog)
                        {
                            Log.Debug("TCP Client: Connection closed by remote host.", this);
                        }

                        await StopAsync();
                        break;
                    }

                    var msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    if (_printToLog)
                    {
                        Log.Debug($"TCP Client ← '{msg}'", this);
                    }

                    _receivedQueue.Enqueue(msg);
                    ReceivedString.DirtyFlag.Invalidate();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetStatus($"Receive error: {ex.Message}", IStatusProvider.StatusLevel.Warning);
                if (_printToLog)
                {
                    Log.Warning($"TCP Client: Receive error: {ex.Message}", this);
                }
            }
            finally
            {
                await StopAsync();
            }
        }

        private async Task SendMessageAsync(string message)
        {
            try
            {
                TcpClientSocket? currentSocket;
                lock (_socketLock)
                {
                    currentSocket = _socket;
                    if (currentSocket == null || !currentSocket.IsConnected)
                        return;
                }

                var data = Encoding.UTF8.GetBytes(message);
                await currentSocket.SendAsync(data, _cts!.Token);

                if (_printToLog)
                {
                    Log.Debug($"TCP Client → '{message}'", this);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
                if (_printToLog)
                {
                    Log.Warning($"TCP Client: Send failed: {ex.Message}", this);
                }

                await StopAsync();
            }
        }

        private void UpdateStatusMessage()
        {
            if (!_lastConnectState)
            {
                SetStatus("Not connected. Enable 'Connect'.", IStatusProvider.StatusLevel.Notice);
            }
            else if (IsConnected.Value)
            {
                SetStatus($"Connected to {_lastHost}:{_lastPort}", IStatusProvider.StatusLevel.Success);
            }
            else
            {
                SetStatus($"Connecting to {_lastHost}:{_lastPort}...", IStatusProvider.StatusLevel.Notice);
            }
        }

        private sealed class TcpClientSocket : IDisposable
        {
            private readonly object _streamLock = new();
            private System.Net.Sockets.TcpClient? _client;
            private NetworkStream? _stream;

            public bool IsConnected => _client?.Connected ?? false;

            public void Dispose()
            {
                _stream?.Dispose();
                _client?.Dispose();
            }

            public async Task ConnectAsync(string host, int port, string? localIpAddress)
            {
                IPAddress? localIp = null;
                if (!string.IsNullOrEmpty(localIpAddress) && localIpAddress != "0.0.0.0 (Any)")
                {
                    IPAddress.TryParse(localIpAddress, out localIp);
                }

                if (localIp != null)
                    _client = new System.Net.Sockets.TcpClient(new IPEndPoint(localIp, 0));
                else
                    _client = new System.Net.Sockets.TcpClient();

                await _client.ConnectAsync(host, port);
                lock (_streamLock)
                {
                    _stream = _client.GetStream();
                }
            }

            public async Task<int> ReceiveAsync(byte[] buffer, CancellationToken ct)
            {
                NetworkStream? currentStream;
                lock (_streamLock)
                {
                    currentStream = _stream;
                    if (currentStream == null) return 0;
                }

                return await currentStream.ReadAsync(buffer, 0, buffer.Length, ct);
            }

            public async Task SendAsync(byte[] data, CancellationToken ct)
            {
                NetworkStream? currentStream;
                lock (_streamLock)
                {
                    currentStream = _stream;
                    if (currentStream == null) return;
                }

                await currentStream.WriteAsync(data, 0, data.Length, ct);
            }
        }

        #region IStatusProvider
        private string _statusMessage = "Disconnected";
        private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;

        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            _statusMessage = message;
            _statusLevel = level;
        }

        public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
        public string GetStatusMessage() => _statusMessage;
        #endregion

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
            public string DisplayName => $"{Name}: {IpAddress}";
        }
        #endregion

        #region ICustomDropdownHolder
        string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value ?? string.Empty : string.Empty;

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
        {
            if (id == LocalIpAddress.Id)
            {
                _networkInterfaces = GetNetworkInterfaces();
                foreach (var adapter in _networkInterfaces) yield return adapter.DisplayName;
            }
        }

        void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
        {
            if (string.IsNullOrEmpty(s) || !i || id != LocalIpAddress.Id) return;
            var foundAdapter = _networkInterfaces.FirstOrDefault(adapter => adapter.DisplayName == s);
            if (foundAdapter != null) LocalIpAddress.SetTypedInputValue(foundAdapter.IpAddress.ToString());
        }
        #endregion
    }
}