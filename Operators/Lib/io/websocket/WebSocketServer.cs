#nullable enable
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Threading;
using System.Text.Json; // Added for JSON parsing
using System.Globalization; // Added for CultureInfo.InvariantCulture

namespace Lib.io.websocket
{
    // MessageParsingMode enum is defined in WebSocketClient.cs and referenced here.

    [Guid("9B4E3C2D-A1F0-4567-89BC-DEF012345678")] // Updated GUID
    public sealed class WebSocketServer : Instance<WebSocketServer>, IStatusProvider, ICustomDropdownHolder, IDisposable
    {
        [Output(Guid = "2A4C9B5E-3D7F-41B0-80F5-1F92D7E0B4C8")]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "3A2B1C0D-E9F8-4756-A3C1-B5E7D9F0A2B4")] // Updated GUID
        public readonly Slot<bool> IsListening = new();

        [Output(Guid = "6F5E4D3C-2B1A-4987-AB9C-DEF012345678")] // Updated GUID
        public readonly Slot<int> ConnectionCount = new();

        // New outputs for incoming client message separation
        [Output(Guid = "6F7E8D9C-0B1A-4323-4567-89ABCDEF0123", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<string> LastReceivedClientString = new();

        [Output(Guid = "7E8D9C0B-1A2F-4234-5678-9ABCDEF01234", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<float>> LastReceivedClientParts = new(new List<float>()); // Changed initialization

        [Output(Guid = "8D9C0B1A-2F3E-4145-6789-ABCDEF012345", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Dict<float>> LastReceivedClientDictionary = new(new Dict<float>(0f)); // Changed initialization

        [Output(Guid = "9C0B1A2F-3E4D-4056-789A-BCDEF0123456", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<bool> ClientMessageWasTrigger = new();

        // Removed ExtractedFloat output
        // [Output(Guid = "4F5E6D7C-8B9A-4012-3456-789ABCDEF012", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // New GUID
        // public readonly Slot<float> ExtractedFloat = new(0f); // New output for single float extraction

        public WebSocketServer()
        {
            Result.UpdateAction = Update;
            IsListening.UpdateAction = Update;
            ConnectionCount.UpdateAction = Update;

            // Added update actions for new outputs
            LastReceivedClientString.UpdateAction = Update;
            LastReceivedClientParts.UpdateAction = Update;
            LastReceivedClientDictionary.UpdateAction = Update;
            ClientMessageWasTrigger.UpdateAction = Update;
            // ExtractedFloat.UpdateAction = Update; // Removed
        }

        private bool _lastListenState;
        private int _lastPort;
        private string? _lastPath;
        private string? _lastLocalIp;
        private string? _lastSentMessage;
        private HttpListener? _listener;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
        private string _statusMessage = "Not listening";
        private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
        private bool _disposed;
        private bool _printToLog;

        // New private fields for handling incoming client messages
        private readonly ConcurrentQueue<string> _receivedClientQueue = new();
        private string _currentClientMessage = string.Empty;
        private List<float> _currentClientParts = new();
        private Dict<float> _currentClientDictionary = new(0f);
        private bool _clientMessageWasTriggeredThisFrame;

        private void Update(EvaluationContext context)
        {
            if (_disposed)
                return;

            _printToLog = PrintToLog.GetValue(context);
            var shouldListen = Listen.GetValue(context);
            var port = Port.GetValue(context);
            var path = Path.GetValue(context);
            var localIp = LocalIpAddress.GetValue(context);

            // New inputs for client message parsing
            var clientParsingMode = (MessageParsingMode)ClientMessageParsingMode.GetValue(context);
            var clientDelimiter = ClientMessageDelimiter.GetValue(context);
            // var keyToExtract = KeyToExtract.GetValue(context); // Removed

            var settingsChanged = shouldListen != _lastListenState || port != _lastPort || path != _lastPath || localIp != _lastLocalIp;
            if (settingsChanged)
            {
                StopListening();
                if (shouldListen)
                {
                    var host = localIp;
                    if (string.IsNullOrEmpty(host) || host.Equals("0.0.0.0") || host.Equals("0.0.0.0 (Listen on all interfaces)"))
                    {
                        host = "+";
                    }
                    else if (!IPAddress.TryParse(host, out _))
                    {
                        host = "+";
                        SetStatus($"Invalid Local IP '{localIp}', defaulting to listen on all interfaces.", IStatusProvider.StatusLevel.Warning);
                    }

                    var urlPath = path?.TrimStart('/') ?? string.Empty;
                    var prefix = $"http://{host}:{port}/" + (!string.IsNullOrEmpty(urlPath) ? $"{urlPath}/" : "");
                    StartListeningInternal(prefix);
                }
                _lastListenState = shouldListen;
                _lastPort = port;
                _lastPath = path;
                _lastLocalIp = localIp;
            }

            IsListening.Value = _listener is { IsListening: true };
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

            _clientMessageWasTriggeredThisFrame = false;
            _currentClientParts.Clear();
            _currentClientDictionary.Clear();
            // ExtractedFloat.Value = 0f; // Removed

            while (_receivedClientQueue.TryDequeue(out var clientMsg))
            {
                _currentClientMessage = clientMsg;
                _clientMessageWasTriggeredThisFrame = true;

                switch (clientParsingMode)
                {
                    case MessageParsingMode.Raw:
                        break;
                    case MessageParsingMode.SpaceSeparated:
                        var stringParts = clientMsg.Split(new[] { clientDelimiter }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in stringParts)
                        {
                            if (TryGetFloatFromObject(part, out var floatValue))
                            {
                                _currentClientParts.Add(floatValue);
                            }
                            else if (_printToLog)
                            {
                                Log.Warning($"WS Server: Could not parse '{part}' as float in SpaceSeparated mode.", this);
                            }
                        }
                        break;
                    case MessageParsingMode.JsonKeyValue:
                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(clientMsg))
                            {
                                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                                    {
                                        if (TryGetFloatFromObject(property.Value, out var floatValue))
                                        {
                                            _currentClientDictionary[property.Name] = floatValue;
                                        }
                                        else if (_printToLog)
                                        {
                                            Log.Warning($"WS Server: Could not parse JSON value '{property.Value}' for key '{property.Name}' as float.", this);
                                        }
                                    }
                                }
                                else
                                {
                                    Log.Warning($"WS Server: Received JSON from client is not an object for key-value parsing: '{clientMsg}'", this);
                                }
                            }
                        }
                        catch (JsonException e)
                        {
                            Log.Warning($"WS Server: Failed to parse incoming client message as JSON: {e.Message}. Message: '{clientMsg}'", this);
                        }
                        break;
                }
            }

            LastReceivedClientString.Value = _currentClientMessage;
            LastReceivedClientParts.Value = _currentClientParts;
            LastReceivedClientDictionary.Value = _currentClientDictionary;

            // Removed single float extraction logic
            // if (!string.IsNullOrEmpty(keyToExtract) && _currentClientDictionary.TryGetValue(keyToExtract, out var extractedValue))
            // {
            //     ExtractedFloat.Value = extractedValue;
            // }
            // else
            // {
            //     ExtractedFloat.Value = 0f;
            // }

            ClientMessageWasTrigger.Value = _clientMessageWasTriggeredThisFrame;

            LastReceivedClientString.DirtyFlag.Invalidate();
            LastReceivedClientParts.DirtyFlag.Invalidate();
            LastReceivedClientDictionary.DirtyFlag.Invalidate();
            ClientMessageWasTrigger.DirtyFlag.Invalidate();
            // ExtractedFloat.DirtyFlag.Invalidate(); // Removed
        }

        private static bool TryGetFloatFromObject(object arg, out float value)
        {
            value = arg switch
            {
                float f => f,
                int i => i,
                bool b => b ? 1f : 0f,
                string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : float.NaN,
                double d => (float)d,
                JsonElement je =>
                    je.ValueKind switch
                    {
                        JsonValueKind.Number => je.TryGetSingle(out var fVal) ? fVal : (je.TryGetDouble(out var dVal) ? (float)dVal : float.NaN),
                        JsonValueKind.True => 1f,
                        JsonValueKind.False => 0f,
                        JsonValueKind.String => float.TryParse(je.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fValString) ? fValString : float.NaN,
                        _ => float.NaN
                    },
                _ => float.NaN
            };
            return !float.IsNaN(value);
        }

        private void StartListeningInternal(string prefix)
        {
            if (_listener is { IsListening: true }) return;

            _listener = new HttpListener();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                SetStatus($"Listening on {prefix.Replace("+", "localhost")}", IStatusProvider.StatusLevel.Success);
                if (_printToLog)
                {
                    Log.Debug($"WS Server: Started listening on {prefix.Replace("+", "localhost")}", this);
                }
                _ = Task.Run(AcceptConnectionsLoop, _cancellationTokenSource.Token);
            }
            catch (HttpListenerException hlex) when (hlex.ErrorCode == 5)
            {
                SetStatus($"Access denied. Try running T3 as Administrator or: netsh http add urlacl url={prefix} user=Everyone", IStatusProvider.StatusLevel.Error);
                if (_printToLog)
                {
                    Log.Error($"WS Server: Access denied starting listener on {prefix}: {hlex.Message}", this);
                }
                _listener?.Close();
                _listener = null;
            }
            catch (Exception e)
            {
                SetStatus($"Failed to start: {e.Message}", IStatusProvider.StatusLevel.Error);
                if (_printToLog)
                {
                    Log.Error($"WS Server: Failed to start listener on {prefix}: {e.Message}", this);
                }
                _listener?.Close();
                _listener = null;
            }
        }

        private async Task AcceptConnectionsLoop()
        {
            try
            {
                while (_listener?.IsListening == true && !_cancellationTokenSource!.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
                        var webSocket = webSocketContext.WebSocket;
                        var clientId = Guid.NewGuid();
                        _clients[clientId] = webSocket;
                        ConnectionCount.DirtyFlag.Invalidate();
                        if (_printToLog)
                        {
                            Log.Debug($"WS Server: Client {clientId} connected.", this);
                        }
                        _ = HandleClient(clientId, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                if (_printToLog)
                {
                    Log.Debug($"WS Server: Listener loop stopped due to shutdown: {e.Message}", this);
                }
            }
            catch (Exception e)
            {
                if (_listener?.IsListening == true)
                {
                    Log.Warning($"WS Server: Listener loop stopped unexpectedly: {e.Message}", this);
                }
            }
        }

        private async Task HandleClient(Guid clientId, WebSocket webSocket)
        {
            var buffer = new byte[8192];
            try
            {
                while (webSocket.State == WebSocketState.Open && !_cancellationTokenSource!.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        if (_printToLog)
                        {
                            Log.Debug($"WS Server ← '{message}' from client {clientId}", this);
                        }
                        _receivedClientQueue.Enqueue(message);
                        LastReceivedClientString.DirtyFlag.Invalidate();
                        LastReceivedClientParts.DirtyFlag.Invalidate();
                        LastReceivedClientDictionary.DirtyFlag.Invalidate();
                        ClientMessageWasTrigger.DirtyFlag.Invalidate();
                        // ExtractedFloat.DirtyFlag.Invalidate(); // Removed
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception e)
            {
                Log.Warning($"WS Server: Error handling client {clientId}: {e.Message}", this);
            }
            finally
            {
                if (_clients.TryRemove(clientId, out _))
                {
                    ConnectionCount.DirtyFlag.Invalidate();
                    if (_printToLog)
                    {
                        Log.Debug($"WS Server: Client {clientId} disconnected.", this);
                    }
                }
                webSocket.Dispose();
            }
        }

        private async Task BroadcastMessageAsync(string message)
        {
            if (_clients.IsEmpty) return;

            var messageBuffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
            var clientsToBroadcast = _clients.Values.ToList();

            if (_printToLog)
            {
                Log.Debug($"WS Server → Broadcast '{message}' to {clientsToBroadcast.Count} clients", this);
            }

            foreach (var client in clientsToBroadcast)
            {
                if (client.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.SendAsync(messageBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"WS Server: Failed to send message to a client: {e.Message}", this);
                    }
                }
            }
        }

        private void StopListening()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _listener?.Stop();

                foreach (var client in _clients.Values)
                {
                    client.Dispose();
                }
                _clients.Clear();

                if (!_lastListenState)
                    SetStatus("Not listening", IStatusProvider.StatusLevel.Notice);
                else if (_printToLog)
                {
                    Log.Debug("WS Server: Stopped listening.", this);
                }

                IsListening.DirtyFlag.Invalidate();
                ConnectionCount.DirtyFlag.Invalidate();
            }
            catch (Exception e)
            {
                Log.Warning($"WS Server: Error stopping server: {e.Message}", this);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Task.Run(StopListening);
            _cancellationTokenSource?.Dispose();
            _listener?.Close();
        }

        public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
        public string GetStatusMessage() => _statusMessage;
        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            _statusMessage = message;
            _statusLevel = level;
        }

        #region ICustomDropdownHolder Implementation
        string ICustomDropdownHolder.GetValueForInput(Guid id)
        {
            if (id == LocalIpAddress.Id)
            {
                return LocalIpAddress.Value;
            }
            if (id == ClientMessageParsingMode.Id)
            {
                return ((MessageParsingMode)ClientMessageParsingMode.Value).ToString();
            }
            return string.Empty;
        }

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
        {
            if (id == LocalIpAddress.Id)
            {
                return GetLocalIPv4Addresses();
            }
            if (id == ClientMessageParsingMode.Id)
            {
                return Enum.GetNames(typeof(MessageParsingMode));
            }
            return Enumerable.Empty<string>();
        }

        void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool isAListItem)
        {
            if (string.IsNullOrEmpty(s) || !isAListItem) return;

            if (id == LocalIpAddress.Id)
            {
                LocalIpAddress.SetTypedInputValue(s.Split(' ')[0]);
            }
            else if (id == ClientMessageParsingMode.Id)
            {
                if (Enum.TryParse(s, out MessageParsingMode mode))
                {
                    ClientMessageParsingMode.SetTypedInputValue((int)mode);
                }
            }
        }

        private static IEnumerable<string> GetLocalIPv4Addresses()
        {
            yield return "0.0.0.0 (Listen on all interfaces)";
            yield return "127.0.0.1";

            if (!NetworkInterface.GetIsNetworkAvailable()) yield break;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        yield return ipInfo.Address.ToString();
                    }
                }
            }
        }
        #endregion

        [Input(Guid = "3D4F5E6A-7B8C-9D0E-F1A2-B3C4D5E6F7A8")]
        public readonly InputSlot<bool> Listen = new();

        [Input(Guid = "1D2E3F4A-5B6C-4789-A0B1-C2D3E4F5A6B7")]
        public readonly InputSlot<int> Port = new(8080);

        [Input(Guid = "8E7D6C5B-4A3F-4123-B9C8-D7E6F5A4B3C2")]
        public readonly InputSlot<string> Path = new();

        [Input(Guid = "0A1B2C3D-4E5F-4678-9012-3456789ABCDE")]
        public readonly InputSlot<string> Message = new();

        [Input(Guid = "A0E35517-337A-4770-985A-34CAC95B5B5F")]
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "4D3C2B1A-0F9E-4876-5432-10FEDCBA9876")]
        public readonly InputSlot<bool> SendTrigger = new();

        [Input(Guid = "73B1F8C9-9A2E-47C0-B1C8-29C34A2E7D01")]
        public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Listen on all interfaces)");

        [Input(Guid = "2E725916-4143-4759-8651-E12185C658D3")]
        public readonly InputSlot<bool> PrintToLog = new();

        [Input(Guid = "4B5A6F7E-8D9C-4501-2345-6789ABCDEF01")]
        public readonly InputSlot<int> ClientMessageParsingMode = new((int)MessageParsingMode.Raw);

        [Input(Guid = "5A6F7E8D-9C0B-4412-3456-789ABCDEF012")]
        public readonly InputSlot<string> ClientMessageDelimiter = new(" ");

        // Removed KeyToExtract input
        // [Input(Guid = "9F0A1B2C-3D4E-4567-8901-23456789ABCDEF")] // New GUID
        // public readonly InputSlot<string> KeyToExtract = new(""); // New input for single float extraction
    }
}