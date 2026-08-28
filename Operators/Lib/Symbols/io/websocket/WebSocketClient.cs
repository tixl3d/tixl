#nullable enable
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using T3.Core.Utils;
// Added for JSON parsing

// Added for CultureInfo.InvariantCulture

namespace Lib.io.websocket
{
    // Moved the enum definition here, outside the class, and made it public.
    internal enum MessageParsingMode
    {
        Raw,
        SpaceSeparated,
        JsonKeyValue
    }

    [Guid("8E9F0A1B-2C3D-4E5F-6A7B-8C9D0E1F2A3B")] // Updated GUID
    internal sealed class WebSocketClient : Instance<WebSocketClient>, IStatusProvider, ICustomDropdownHolder, IDisposable
    {
        private readonly List<string> _messageHistory = new();
        private readonly ConcurrentQueue<string> _receivedQueue = new();
        private readonly object _socketLock = new();

        [Input(Guid = "A1B2C3D4-E5F6-4789-0123-456789ABCDEF")]
        public readonly InputSlot<bool> Connect = new();

        [Input(Guid = "1E2D3C4B-5A6F-4879-0123-456789ABCDEF")]
        public readonly InputSlot<string> Delimiter = new(" ");

        [Output(Guid = "C1D2E3F4-5A6B-4789-0C1D-2E3F4A5B6C7D")] // Updated GUID
        public readonly Slot<bool> IsConnected = new();

        [Input(Guid = "D1E2F3A4-B5C6-4789-0123-456789ABCDEF")]
        public readonly InputSlot<int> ListLength = new(10);

        [Input(Guid = "59074D76-1F4F-406A-B512-5813F4E3420E")]
        public readonly MultiInputSlot<string> MessageParts = new();

        [Input(Guid = "0F1E2D3C-4B5A-4968-7012-3456789ABCDE")]
        public readonly InputSlot<int> ParsingMode = new((int)MessageParsingMode.Raw);

        [Input(Guid = "5E725916-4143-4759-8651-E12185C658D3")]
        public readonly InputSlot<bool> PrintToLog = new();

        [Output(Guid = "2D3C4B5A-6F7E-4789-0123-456789ABCDEF", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Dict<float>> ReceivedDictionary = new(new Dict<float>(0f)); // Corrected initialization

        [Output(Guid = "F2E1D0C9-B8A7-4654-7321-0FEDCBA98765", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // Updated GUID
        public readonly Slot<List<string>> ReceivedLines = new();

        [Output(Guid = "3C4B5A6F-7E8D-4690-1234-56789ABCDEF0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<float>> ReceivedParts = new(new List<float>()); // Corrected initialization

        [Output(Guid = "7C8D9E0A-1B2C-43F5-89DE-1234567890AB", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // Updated GUID
        public readonly Slot<string> ReceivedString = new();

        [Input(Guid = "216A0356-EF4A-413A-A656-7497127E31D4")]
        public readonly InputSlot<bool> SendOnChange = new(true);

        [Input(Guid = "C7AC22C0-A31E-41F6-B29D-D40956E6688B")]
        public readonly InputSlot<bool> SendTrigger = new();

        [Input(Guid = "82933C40-DA9E-4340-A227-E9BACF6E2063")]
        public readonly InputSlot<string> Separator = new(" ");

        [Input(Guid = "B1C2D3E4-F5A6-4789-0123-456789ABCDEF")]
        public readonly InputSlot<string> Url = new("ws://localhost:8080");

        [Output(Guid = "9B8A7C6D-5E4F-4012-3456-7890ABCDEF12", DirtyFlagTrigger = DirtyFlagTrigger.Animated)] // Updated GUID
        public readonly Slot<bool> WasTrigger = new();

        private CancellationTokenSource? _cts;

        private readonly Dict<float> _currentReceivedDictionary = new(0f);
        private readonly List<float> _currentReceivedParts = new();
        private bool _disposed;
        private bool _lastConnectState;
        private string? _lastSentMessage;
        private string? _lastUrl;
        private bool _printToLog;

        private ClientWebSocket? _webSocket;

        public WebSocketClient()
        {
            ReceivedString.UpdateAction = Update;
            ReceivedLines.UpdateAction = Update;
            WasTrigger.UpdateAction = Update;
            IsConnected.UpdateAction = Update;
            ReceivedDictionary.UpdateAction = Update;
            ReceivedParts.UpdateAction = Update;
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
            var url = Url.GetValue(context);

            // New inputs for parsing
            var parsingMode = (MessageParsingMode)ParsingMode.GetValue(context);
            var delimiter = Delimiter.GetValue(context) ?? string.Empty;

            var settingsChanged = shouldConnect != _lastConnectState || url != _lastUrl;
            if (settingsChanged)
            {
                _ = HandleConnectionChange(shouldConnect, url);
            }

            HandleMessageSending(context);
            HandleReceivedMessages(context, parsingMode, delimiter);
            UpdateStatusMessage();
        }

        private async Task HandleConnectionChange(bool shouldConnect, string? url)
        {
            await StopAsync();
            _lastConnectState = shouldConnect;
            _lastUrl = url;

            if (shouldConnect && !string.IsNullOrWhiteSpace(url))
            {
                await StartAsync(url);
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

        private void HandleReceivedMessages(EvaluationContext context, MessageParsingMode parsingMode, string delimiter)
        {
            var listLength = ListLength.GetValue(context).Clamp(1, 1000);
            var wasTriggered = false;

            _currentReceivedDictionary.Clear();
            _currentReceivedParts.Clear();

            while (_receivedQueue.TryDequeue(out var msg))
            {
                ReceivedString.Value = msg;
                _messageHistory.Add(msg);
                wasTriggered = true;

                switch (parsingMode)
                {
                    case MessageParsingMode.Raw:
                        break;
                    case MessageParsingMode.SpaceSeparated:
                        var stringParts = msg.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in stringParts)
                        {
                            if (TryGetFloatFromObject(part, out var floatValue))
                            {
                                _currentReceivedParts.Add(floatValue);
                            }
                            else if (_printToLog)
                            {
                                Log.Warning($"WS Client: Could not parse '{part}' as float in SpaceSeparated mode.", this);
                            }
                        }

                        break;
                    case MessageParsingMode.JsonKeyValue:
                        try
                        {
                            using (JsonDocument doc = JsonDocument.Parse(msg))
                            {
                                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                                    {
                                        if (TryGetFloatFromObject(property.Value, out var floatValue))
                                        {
                                            _currentReceivedDictionary[property.Name] = floatValue;
                                        }
                                        else if (_printToLog)
                                        {
                                            Log.Warning($"WS Client: Could not parse JSON value '{property.Value}' for key '{property.Name}' as float.", this);
                                        }
                                    }
                                }
                                else
                                {
                                    Log.Warning($"WS Client: Received JSON is not an object for key-value parsing: '{msg}'", this);
                                }
                            }
                        }
                        catch (JsonException e)
                        {
                            Log.Warning($"WS Client: Failed to parse incoming message as JSON: {e.Message}. Message: '{msg}'", this);
                        }

                        break;
                }
            }

            ReceivedParts.Value = _currentReceivedParts;
            ReceivedDictionary.Value = _currentReceivedDictionary;

            while (_messageHistory.Count > listLength)
                _messageHistory.RemoveAt(0);

            ReceivedLines.Value = new List<string>(_messageHistory);
            WasTrigger.Value = wasTriggered;
            IsConnected.Value = _webSocket?.State == WebSocketState.Open;

            ReceivedParts.DirtyFlag.Invalidate();
            ReceivedDictionary.DirtyFlag.Invalidate();
        }

        private static bool TryGetFloatFromObject(object arg, out float value)
        {
            value = arg switch
                        {
                            float f  => f,
                            int i    => i,
                            bool b   => b ? 1f : 0f,
                            string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : float.NaN,
                            double d => (float)d,
                            JsonElement je =>
                                je.ValueKind switch
                                    {
                                        JsonValueKind.Number => je.TryGetSingle(out var fVal)
                                                                    ? fVal
                                                                    : je.TryGetDouble(out var dVal)
                                                                        ? (float)dVal
                                                                        : float.NaN,
                                        JsonValueKind.True  => 1f,
                                        JsonValueKind.False => 0f,
                                        JsonValueKind.String => float.TryParse(je.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                                                               out var fValString)
                                                                    ? fValString
                                                                    : float.NaN,
                                        _ => float.NaN
                                    },
                            _ => float.NaN
                        };
            return !float.IsNaN(value);
        }

        private async Task StartAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                SetStatus($"Invalid URL: {url}", IStatusProvider.StatusLevel.Error);
                if (_printToLog)
                {
                    Log.Error($"WS Client: Invalid URL '{url}'", this);
                }

                return;
            }

            lock (_socketLock)
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
            }

            try
            {
                SetStatus($"Connecting to {url}...", IStatusProvider.StatusLevel.Notice);
                if (_printToLog)
                {
                    Log.Debug($"WS Client: Attempting to connect to {url}...", this);
                }

                await _webSocket!.ConnectAsync(uri, _cts.Token);
                SetStatus($"Connected to {url}", IStatusProvider.StatusLevel.Success);
                if (_printToLog)
                {
                    Log.Debug($"WS Client: Connected to {url}", this);
                }

                _ = Task.Run(ReceiveLoop);
            }
            catch (Exception e)
            {
                SetStatus($"Connect failed: {e.Message}", IStatusProvider.StatusLevel.Error);
                if (_printToLog)
                {
                    Log.Error($"WS Client: Connect failed to {url}: {e.Message}", this);
                }

                lock (_socketLock)
                {
                    _webSocket?.Dispose();
                    _webSocket = null;
                }
            }
            finally
            {
                IsConnected.DirtyFlag.Invalidate();
            }
        }

        private async Task StopAsync()
        {
            ClientWebSocket? socketToClose = null;
            CancellationTokenSource? ctsToDispose = null;

            lock (_socketLock)
            {
                if (_webSocket != null)
                {
                    socketToClose = _webSocket;
                    _webSocket = null;
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

                if (socketToClose != null && (socketToClose.State == WebSocketState.Open || socketToClose.State == WebSocketState.CloseSent))
                {
                    if (_printToLog)
                    {
                        Log.Debug($"WS Client: Closing WebSocket connection.", this);
                    }

                    try
                    {
                        await socketToClose.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                    }
                    catch (WebSocketException wsex)
                    {
                        Log.Warning($"WS Client: Error during CloseAsync: {wsex.Message}", this);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"WS Client: Error stopping WebSocket: {e.Message}", this);
            }
            finally
            {
                socketToClose?.Dispose();
                ctsToDispose?.Dispose();

                SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
                if (_printToLog)
                {
                    Log.Debug("WS Client: Disconnected.", this);
                }

                IsConnected.DirtyFlag.Invalidate();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    ClientWebSocket? currentSocket;
                    CancellationToken cancellationToken;

                    lock (_socketLock)
                    {
                        currentSocket = _webSocket;
                        cancellationToken = _cts?.Token ?? CancellationToken.None;
                        if (currentSocket == null || currentSocket.State != WebSocketState.Open)
                            break;
                    }

                    var result = await currentSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (_printToLog)
                        {
                            Log.Debug("WS Client: Received close message from server.", this);
                        }

                        await StopAsync();
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        if (_printToLog)
                        {
                            Log.Debug($"WS Client ← '{msg}'", this);
                        }

                        _receivedQueue.Enqueue(msg);
                        ReceivedString.DirtyFlag.Invalidate();
                        ReceivedLines.DirtyFlag.Invalidate();
                        WasTrigger.DirtyFlag.Invalidate();
                        ReceivedDictionary.DirtyFlag.Invalidate();
                        ReceivedParts.DirtyFlag.Invalidate();
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                {
                    SetStatus($"Receive error: {ex.Message}", IStatusProvider.StatusLevel.Warning);
                    if (_printToLog)
                    {
                        Log.Warning($"WS Client: Receive error: {ex.Message}", this);
                    }
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
                ClientWebSocket? currentSocket;
                lock (_socketLock)
                {
                    currentSocket = _webSocket;
                    if (currentSocket?.State != WebSocketState.Open)
                        return;
                }

                var data = Encoding.UTF8.GetBytes(message);
                await currentSocket.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);

                if (_printToLog)
                {
                    Log.Debug($"WS Client → '{message}'", this);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Send failed: {ex.Message}", IStatusProvider.StatusLevel.Warning);
                if (_printToLog)
                {
                    Log.Warning($"WS Client: Send failed: {ex.Message}", this);
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
                SetStatus($"Connected to {_lastUrl}", IStatusProvider.StatusLevel.Success);
            }
            else
            {
                SetStatus($"Connecting to {_lastUrl}...", IStatusProvider.StatusLevel.Notice);
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

        #region ICustomDropdownHolder Implementation
        string ICustomDropdownHolder.GetValueForInput(Guid inputId)
        {
            if (inputId == ParsingMode.Id)
            {
                return ((MessageParsingMode)ParsingMode.Value).ToString();
            }

            return string.Empty;
        }

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
        {
            if (inputId == ParsingMode.Id)
            {
                return Enum.GetNames(typeof(MessageParsingMode));
            }

            return Empty<string>();
        }

        void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
        {
            if (inputId == ParsingMode.Id && isAListItem)
            {
                if (Enum.TryParse(selected, out MessageParsingMode mode))
                {
                    ParsingMode.SetTypedInputValue((int)mode);
                }
            }
        }
        #endregion
    }
}