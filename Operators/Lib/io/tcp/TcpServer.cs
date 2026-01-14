#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Lib.io.tcp
{
    [Guid("0F1E2D3C-4B5A-4678-9012-3456789ABCDE")]
    public sealed class TcpServer : Instance<TcpServer>
                                  , IStatusProvider, ICustomDropdownHolder, IDisposable
    {
        private readonly ConcurrentDictionary<Guid, System.Net.Sockets.TcpClient> _clients = new();

        [Output(Guid = "89ABCDEF-0123-4567-89AB-CDEF01234567")]
        public readonly Slot<int> ConnectionCount = new();

        [Output(Guid = "789ABCDE-F012-4345-6789-ABCDEF012345")]
        public readonly Slot<bool> IsListening = new();

        [Input(Guid = "9A0B1C2D-3E4F-4567-8901-23456789ABC0")]
        public readonly InputSlot<bool> Listen = new();

        [Input(Guid = "A0B1C2D3-E4F5-4678-9012-3456789ABCDE")]
        public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0");

        [Input(Guid = "C2D3E4F5-A6B7-4890-1234-567890ABCDEF")]
        public readonly InputSlot<string> Message = new();

        [Input(Guid = "B1C2D3E4-F5A6-4789-0123-456789ABCDEF")]
        public readonly InputSlot<int> Port = new(8080);

        [Input(Guid = "F5A6B7C8-D9E0-4123-4567-890ABCDEF123")]
        public readonly InputSlot<bool> PrintToLog = new();

        [Output(Guid = "6789ABCD-EF01-4234-5678-90ABCDEF0123")]
        public readonly Slot<Command> Result = new();

        [Input(Guid = "D3E4F5A6-B7C8-4901-2345-67890ABCDEF1")]
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "E4F5A6B7-C8D9-4012-3456-7890ABCDEF12")]
        public readonly InputSlot<bool> SendTrigger = new();

        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;

        private bool _lastListenState;
        private string? _lastLocalIp;
        private int _lastPort;
        private string? _lastSentMessage;
        private TcpListener? _listener;
        private bool _printToLog;
        private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
        private string _statusMessage = "Not listening";

        public TcpServer()
        {
            Result.UpdateAction = Update;
            IsListening.UpdateAction = Update;
            ConnectionCount.UpdateAction = Update;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Task.Run(StopListening);
        }

        public IStatusProvider.StatusLevel GetStatusLevel()
        {
            return _statusLevel;
        }

        public string GetStatusMessage()
        {
            return _statusMessage;
        }

        private void Update(EvaluationContext context)
        {
            if (_disposed)
                return;

            _printToLog = PrintToLog.GetValue(context);
            var shouldListen = Listen.GetValue(context);
            var localIp = LocalIpAddress.GetValue(context);
            var port = Port.GetValue(context);

            var settingsChanged = shouldListen != _lastListenState || localIp != _lastLocalIp || port != _lastPort;
            if (settingsChanged)
            {
                StopListening();
                if (shouldListen)
                {
                    StartListening(localIp, port);
                }

                _lastListenState = shouldListen;
                _lastLocalIp = localIp;
                _lastPort = port;
            }

            IsListening.Value = _listener != null;
            ConnectionCount.Value = _clients.Count;

            var message = Message.GetValue(context);
            var sendOnChange = SendOnChange.GetValue(context);
            var triggerSend = SendTrigger.GetValue(context);

            var messageChanged = message != _lastSentMessage;

            if (triggerSend || (sendOnChange && messageChanged))
            {
                if (!string.IsNullOrEmpty(message))
                {
                    _ = BroadcastMessageAsync(message);
                    _lastSentMessage = message;
                }

                if (triggerSend)
                    SendTrigger.SetTypedInputValue(false);
            }
        }

        private void StartListening(string? localIpAddress, int port)
        {
            if (_listener != null) return;

            IPAddress? listenIp;
            if (string.IsNullOrEmpty(localIpAddress) || localIpAddress == "0.0.0.0 (Any)")
            {
                listenIp = IPAddress.Any;
            }
            else if (!IPAddress.TryParse(localIpAddress, out listenIp))
            {
                SetStatus($"Invalid Local IP '{localIpAddress}'. Defaulting to IPAddress.Any.", IStatusProvider.StatusLevel.Warning);
                if (_printToLog)
                {
                    Log.Warning($"TCP Server: Invalid Local IP '{localIpAddress}', defaulting to IPAddress.Any.", this);
                }

                listenIp = IPAddress.Any;
            }

            _listener = new TcpListener(listenIp, port);
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _listener.Start();
                SetStatus($"Listening on {listenIp}:{port}", IStatusProvider.StatusLevel.Success);
                if (_printToLog)
                {
                    Log.Debug($"TCP Server: Started listening on {listenIp}:{port}", this);
                }

                _ = Task.Run(AcceptConnectionsLoop, _cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                SetStatus($"Failed to start: {e.Message}", IStatusProvider.StatusLevel.Error);
                if (_printToLog)
                {
                    Log.Error($"TCP Server: Failed to start listening on {listenIp}:{port}: {e.Message}", this);
                }

                _listener?.Stop();
                _listener = null;
            }
        }

        private async Task AcceptConnectionsLoop()
        {
            try
            {
                var cts = _cancellationTokenSource;
                var listener = _listener;

                if (cts == null || listener == null) return;

                while (!cts.IsCancellationRequested)
                {
                    System.Net.Sockets.TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException sex) when (sex.SocketErrorCode == SocketError.OperationAborted)
                    {
                        break;
                    }

                    var clientId = Guid.NewGuid();
                    _clients[clientId] = client;
                    ConnectionCount.DirtyFlag.Invalidate();
                    if (_printToLog)
                    {
                        Log.Debug($"TCP Server: Client {clientId} connected from {client.Client.RemoteEndPoint}", this);
                    }

                    _ = HandleClient(clientId, client);
                }
            }
            catch (OperationCanceledException)
            {
                /* Expected */
            }
            catch (Exception e)
            {
                if (!_cancellationTokenSource!.IsCancellationRequested)
                {
                    Log.Warning($"TCP Server: Listener loop stopped unexpectedly: {e.Message}", this);
                }
            }
        }

        private async Task HandleClient(Guid clientId, System.Net.Sockets.TcpClient client)
        {
            var buffer = new byte[8192];
            try
            {
                await using var stream = client.GetStream();

                var cts = _cancellationTokenSource;
                if (cts == null) return;

                while (!cts.IsCancellationRequested && client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead == 0)
                    {
                        if (_printToLog)
                        {
                            Log.Debug($"TCP Server: Client {clientId} disconnected gracefully.", this);
                        }

                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    if (_printToLog)
                    {
                        Log.Debug($"TCP Server ← '{message}' from client {clientId}", this);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                /* Expected */
            }
            catch (Exception e)
            {
                if (_printToLog)
                {
                    Log.Warning($"TCP Server: Error handling client {clientId}: {e.Message}", this);
                }
            }
            finally
            {
                if (_clients.TryRemove(clientId, out var removedClient))
                {
                    removedClient.Dispose();
                    ConnectionCount.DirtyFlag.Invalidate();
                    if (_printToLog)
                    {
                        Log.Debug($"TCP Server: Client {clientId} removed from active connections.", this);
                    }
                }
            }
        }

        private async Task BroadcastMessageAsync(string message)
        {
            if (_clients.IsEmpty) return;

            var data = Encoding.UTF8.GetBytes(message);
            var clientsToBroadcast = _clients.Values.ToList();

            if (_printToLog)
            {
                Log.Debug($"TCP Server → Broadcast '{message}' to {clientsToBroadcast.Count} clients", this);
            }

            foreach (var client in clientsToBroadcast)
            {
                if (client.Connected)
                {
                    try
                    {
                        var stream = client.GetStream();
                        var cts = _cancellationTokenSource;
                        if (cts == null) continue;

                        await stream.WriteAsync(data, 0, data.Length, cts.Token);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"TCP Server: Failed to send to a client: {e.Message}", this);
                    }
                }
            }
        }

        private void StopListening()
        {
            TcpListener? listenerToStop = null;
            CancellationTokenSource? ctsToDispose = null;

            lock (_clients)
            {
                if (_listener != null)
                {
                    listenerToStop = _listener;
                    _listener = null;
                }

                if (_cancellationTokenSource != null)
                {
                    ctsToDispose = _cancellationTokenSource;
                    _cancellationTokenSource = null;
                }
            }

            try
            {
                ctsToDispose?.Cancel();
                listenerToStop?.Stop();

                foreach (var client in _clients.Values)
                {
                    client.Dispose();
                }

                _clients.Clear();

                if (!_lastListenState)
                    SetStatus("Not listening", IStatusProvider.StatusLevel.Notice);
                else if (_printToLog)
                {
                    Log.Debug("TCP Server: Stopped listening.", this);
                }

                IsListening.DirtyFlag.Invalidate();
                ConnectionCount.DirtyFlag.Invalidate();
            }
            catch (Exception e)
            {
                Log.Warning($"TCP Server: Error stopping server: {e.Message}", this);
            }
            finally
            {
                ctsToDispose?.Dispose();
            }
        }

        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            _statusMessage = message;
            _statusLevel = level;
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
            public string DisplayName => $"{Name}: {IpAddress}";
        }
        #endregion

        #region ICustomDropdownHolder Implementation
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