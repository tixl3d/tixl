#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
// Added for NetworkInterface

namespace Lib.io.tcp
{
    [Guid("0F1E2D3C-4B5A-4678-9012-3456789ABCDE")] // Updated GUID
    public sealed class TcpServer : Instance<TcpServer>
                                  , IStatusProvider, ICustomDropdownHolder, IDisposable // Added ICustomDropdownHolder
    {
        private readonly ConcurrentDictionary<Guid, System.Net.Sockets.TcpClient> _clients = new();

        [Output(Guid = "89ABCDEF-0123-4567-89AB-CDEF01234567")] // Updated GUID
        public readonly Slot<int> ConnectionCount = new();

        [Output(Guid = "789ABCDE-F012-4345-6789-ABCDEF012345")] // Updated GUID
        public readonly Slot<bool> IsListening = new();

        [Input(Guid = "9A0B1C2D-3E4F-4567-8901-23456789ABC0")] // Updated GUID
        public readonly InputSlot<bool> Listen = new();

        [Input(Guid = "A0B1C2D3-E4F5-4678-9012-3456789ABCDE")] // Updated GUID
        public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)"); // New input slot with default

        [Input(Guid = "C2D3E4F5-A6B7-4890-1234-567890ABCDEF")] // Updated GUID
        public readonly InputSlot<string> Message = new();

        [Input(Guid = "B1C2D3E4-F5A6-4789-0123-456789ABCDEF")] // Updated GUID
        public readonly InputSlot<int> Port = new(8080);

        [Input(Guid = "F5A6B7C8-D9E0-4123-4567-890ABCDEF123")] // New GUID for PrintToLog
        public readonly InputSlot<bool> PrintToLog = new();

        [Output(Guid = "6789ABCD-EF01-4234-5678-90ABCDEF0123")] // Updated GUID
        public readonly Slot<Command> Result = new();

        [Input(Guid = "D3E4F5A6-B7C8-4901-2345-67890ABCDEF1")] // Updated GUID
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "E4F5A6B7-C8D9-4012-3456-7890ABCDEF12")] // Updated GUID
        public readonly InputSlot<bool> SendTrigger = new();

        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;

        private bool _lastListenState;
        private string? _lastLocalIp; // Added for dropdown implementation
        private int _lastPort;
        private string? _lastSentMessage;
        private TcpListener? _listener;
        private bool _printToLog; // Added for PrintToLog functionality
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

            // Do not await StopListening directly in Dispose as Dispose should not block.
            // Run it as a fire-and-forget task.
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

            _printToLog = PrintToLog.GetValue(context); // Update printToLog flag
            var shouldListen = Listen.GetValue(context);
            var localIp = LocalIpAddress.GetValue(context); // Get the selected local IP
            var port = Port.GetValue(context);

            var settingsChanged = shouldListen != _lastListenState || localIp != _lastLocalIp || port != _lastPort; // Included localIp
            if (settingsChanged)
            {
                StopListening();
                if (shouldListen)
                {
                    StartListening(localIp, port); // Pass localIp to StartListening
                }

                _lastListenState = shouldListen;
                _lastLocalIp = localIp; // Store the last local IP
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

            IPAddress? listenIp; // Initialize to avoid CS8600
            if (string.IsNullOrEmpty(localIpAddress) || localIpAddress == "0.0.0.0 (Any)")
            {
                listenIp = IPAddress.Any;
            }
            else if (!IPAddress.TryParse(localIpAddress, out listenIp)) // TryParse will assign to listenIp if successful
            {
                SetStatus($"Invalid Local IP '{localIpAddress}'. Defaulting to IPAddress.Any.", IStatusProvider.StatusLevel.Warning);
                if (_printToLog)
                {
                    Log.Warning($"TCP Server: Invalid Local IP '{localIpAddress}', defaulting to IPAddress.Any.", this);
                }

                listenIp = IPAddress.Any; // Ensure it's explicitly set if parsing failed
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
                // Capture CancellationTokenSource and listener outside the loop to avoid race conditions
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
                        // Expected when listener is stopped
                        break;
                    }
                    catch (SocketException sex) when (sex.SocketErrorCode == SocketError.OperationAborted)
                    {
                        // Expected when listener is stopped
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

                // Capture CancellationTokenSource outside the loop
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
                    // Additional processing of received messages can be done here.
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
                        // Capture CancellationTokenSource outside the loop
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
            // Capture and nullify resources within a lock
            TcpListener? listenerToStop = null;
            CancellationTokenSource? ctsToDispose = null;

            lock (_clients) // Reusing _clients lock, or could define a new _listenerLock
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
                ctsToDispose?.Cancel(); // Cancel first
                listenerToStop?.Stop(); // Stop the listener

                foreach (var client in _clients.Values)
                {
                    client.Dispose(); // Synchronous dispose of client sockets
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
                ctsToDispose?.Dispose(); // Dispose CTS after cancelling and stopping
            }
        }

        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            _statusMessage = message;
            _statusLevel = level;
        }

        #region ICustomDropdownHolder Implementation
        string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;
        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id) => id == LocalIpAddress.Id ? GetLocalIPv4Addresses() : [];

        void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
        {
            if (string.IsNullOrEmpty(s) || !i || id != LocalIpAddress.Id) return;
            LocalIpAddress.SetTypedInputValue(s.Split(' ')[0]);
        }

        private static IEnumerable<string> GetLocalIPv4Addresses()
        {
            yield return "0.0.0.0 (Any)"; // Option to listen on all available interfaces
            yield return "127.0.0.1"; // Loopback address

            if (!NetworkInterface.GetIsNetworkAvailable()) yield break;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Only consider operational and non-loopback interfaces
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    // Only consider IPv4 addresses
                    if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        yield return ipInfo.Address.ToString();
                    }
                }
            }
        }
        #endregion
    }
}