#nullable enable
using System.Net.Sockets;
using System.Threading;
using T3.Core.Utils;

namespace Lib.io.tcp
{
    [Guid("A2B3C4D5-E6F7-4890-1234-567890ABCDEF")] // Updated GUID
    internal sealed class TcpClient : Instance<TcpClient>
, IStatusProvider, IDisposable
    {
        [Output(Guid = "F1E0D9C8-7B6A-4543-210F-EDCBA9876543", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // Updated GUID
        public readonly Slot<string> ReceivedString = new();

        [Output(Guid = "D5C4B3A2-E1F0-4987-6543-210FEDCBA987", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // Updated GUID
        public readonly Slot<List<string>> ReceivedLines = new();

        [Output(Guid = "1A2B3C4D-5E6F-4789-A0B1-C2D3E4F5A6B7", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // Updated GUID
        public readonly Slot<bool> WasTrigger = new();

        [Output(Guid = "3B4C5D6E-7F8A-4901-2345-67890ABCDEF1")] // Updated GUID
        public readonly Slot<bool> IsConnected = new();

        public TcpClient()
        {
            ReceivedString.UpdateAction = Update;
            ReceivedLines.UpdateAction = Update;
            WasTrigger.UpdateAction = Update;
            IsConnected.UpdateAction = Update;
        }

        private TcpClientSocket? _socket;
        private CancellationTokenSource? _cts;
        private bool _lastConnectState;
        private string? _lastHost;
        private int _lastPort;
        private string? _lastSentMessage;
        private readonly object _socketLock = new();
        private readonly ConcurrentQueue<string> _receivedQueue = new();
        private readonly List<string> _messageHistory = new();
        private bool _printToLog;
        private bool _disposed;

        private void Update(EvaluationContext context)
        {
            if (_disposed)
                return;

            _printToLog = PrintToLog.GetValue(context); // Get value for PrintToLog
            var shouldConnect = Connect.GetValue(context);
            var host = Host.GetValue(context);
            var port = Port.GetValue(context);

            var settingsChanged = shouldConnect != _lastConnectState || host != _lastHost || port != _lastPort;
            if (settingsChanged)
            {
                _ = HandleConnectionChange(shouldConnect, host, port);
            }

            HandleMessageSending(context);
            HandleReceivedMessages(context);
            UpdateStatusMessage();
        }

        private async Task HandleConnectionChange(bool shouldConnect, string? host, int port)
        {
            await StopAsync();
            _lastConnectState = shouldConnect;
            _lastHost = host;
            _lastPort = port;

            if (shouldConnect && !string.IsNullOrWhiteSpace(host))
            {
                await StartAsync(host, port);
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

        private async Task StartAsync(string host, int port)
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
                await _socket!.ConnectAsync(host, port, _cts.Token);
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

            // Use a lock to safely capture the current socket and CTS
            lock (_socketLock)
            {
                if (_socket != null)
                {
                    socketToDispose = _socket;
                    _socket = null; // Clear it within the lock to prevent further use
                }
                if (_cts != null)
                {
                    ctsToDispose = _cts;
                    _cts = null; // Clear it within the lock
                }
            }

            try
            {
                // Cancel the token source outside the lock
                ctsToDispose?.Cancel();

                if (_printToLog)
                {
                    Log.Debug($"TCP Client: Stopping connection.", this);
                }

                // Dispose resources outside the lock (they are synchronous here)
                socketToDispose?.Dispose();
                ctsToDispose?.Dispose(); // Dispose CTS after cancelling

                SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
                if (_printToLog)
                {
                    Log.Debug("TCP Client: Disconnected.", this);
                }
                IsConnected.DirtyFlag.Invalidate();
            }
            catch (Exception e)
            {
                // Catch any other unexpected errors during the stop process
                Log.Warning($"TCP Client: Stop error: {e.Message}", this);
            }

            await Task.Yield(); // Add an await to satisfy CS1998 warning
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
                        cancellationToken = _cts?.Token ?? CancellationToken.None; // Capture token safely
                        if (currentSocket == null || !currentSocket.IsConnected)
                            break;
                    }

                    var bytesRead = await currentSocket!.ReceiveAsync(buffer, cancellationToken);
                    if (bytesRead == 0) // Connection closed
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
            catch (OperationCanceledException) { /* Expected during cancellation */ }
            catch (Exception ex)
            {
                // Catch socket exceptions, network errors, etc.
                SetStatus($"Receive error: {ex.Message}", IStatusProvider.StatusLevel.Warning);
                if (_printToLog)
                {
                    Log.Warning($"TCP Client: Receive error: {ex.Message}", this);
                }
            }
            finally
            {
                // Ensure StopAsync is called regardless of how the loop exits
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
                await currentSocket!.SendAsync(data, _cts!.Token);

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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Do not await StopAsync directly in Dispose as Dispose should not block.
            // Run it as a fire-and-forget task. The internal StopAsync handles its own exceptions.
            Task.Run(StopAsync);
            _receivedQueue.Clear();
            _messageHistory.Clear();
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

        [Input(Guid = "C2D3E4F5-A6B7-4890-1234-567890ABCDEF")] // Updated GUID
        public readonly InputSlot<bool> Connect = new();

        [Input(Guid = "F1E2D3C4-B5A6-4789-0123-456789ABCDEF")] // Updated GUID
        public readonly InputSlot<string> Host = new("localhost");

        [Input(Guid = "A3B4C5D6-E7F8-4901-2345-67890ABCDEF1")] // Updated GUID
        public readonly InputSlot<int> Port = new(8080);

        [Input(Guid = "D4E5F6A7-B8C9-4012-3456-7890ABCDEF12")] // Updated GUID
        public readonly InputSlot<int> ListLength = new(10);

        [Input(Guid = "B5C6D7E8-F9A0-4123-4567-890ABCDEF123")] // Updated GUID
        public readonly MultiInputSlot<string> MessageParts = new();

        [Input(Guid = "E6F7A8B9-C0D1-4234-5678-90ABCDEF1234")] // Updated GUID
        public readonly InputSlot<string> Separator = new(" ");

        [Input(Guid = "F7A8B9C0-D1E2-4345-6789-0ABCDEF12345")] // Updated GUID
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "1B2C3D4E-5F6A-4789-0123-456789ABCDEF")] // Updated GUID
        public readonly InputSlot<bool> SendTrigger = new();

        [Input(Guid = "3C4D5E6F-7A8B-4901-2345-67890ABCDEF1")] // Updated GUID
        public readonly InputSlot<bool> PrintToLog = new();

        private class TcpClientSocket : IDisposable
        {
            private System.Net.Sockets.TcpClient? _client;
            private NetworkStream? _stream;
            private readonly object _streamLock = new(); // Used for stream access synchronization

            public bool IsConnected => _client?.Connected ?? false;

            public async Task ConnectAsync(string host, int port, CancellationToken ct)
            {
                _client = new System.Net.Sockets.TcpClient();
                await _client.ConnectAsync(host, port); // This is an awaitable call
                _stream = _client.GetStream();
            }

            public async Task<int> ReceiveAsync(byte[] buffer, CancellationToken ct)
            {
                NetworkStream? currentStream;
                lock (_streamLock) // Lock for safe access to _stream
                {
                    currentStream = _stream;
                    if (currentStream == null) return 0;
                }
                return await currentStream.ReadAsync(buffer, 0, buffer.Length, ct); // This is an awaitable call
            }

            public async Task SendAsync(byte[] data, CancellationToken ct)
            {
                NetworkStream? currentStream;
                lock (_streamLock) // Lock for safe access to _stream
                {
                    currentStream = _stream;
                    if (currentStream == null) return;
                }
                await currentStream.WriteAsync(data, 0, data.Length, ct); // This is an awaitable call
            }

            public void Dispose()
            {
                // Synchronous disposal of underlying resources.
                // It's generally safe to dispose of NetworkStream and TcpClient synchronously.
                _stream?.Dispose();
                _client?.Dispose();
            }
        }
    }
}