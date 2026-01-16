#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Lib.io.http;

/// <summary>
/// A simple HTTP server operator for T3 to serve a user-provided HTML string.
/// </summary>
[Guid("1D2E3F4A-5B6C-4789-0123-456789ABCDEF")] // Updated GUID
internal sealed class WebServer : Instance<WebServer>, IStatusProvider, ICustomDropdownHolder, IDisposable // Added ICustomDropdownHolder
{
    #region Constructor
    public WebServer()
    {
        IsRunning.UpdateAction = Update;
        PortOutput.UpdateAction = Update;
    }
    #endregion

    #region IDisposable Implementation
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopServer(); // Stop server and cleanup listener
        _cancellationTokenSource?.Dispose(); // Dispose CancellationTokenSource
    }
    #endregion

    #region Main Update Loop
    private void Update(EvaluationContext context)
    {
        if (_disposed) return;

        _printToLog = PrintToLog.GetValue(context); // Update PrintToLog flag
        var shouldListen = Listen.GetValue(context);
        var localIp = LocalIpAddress.GetValue(context); // Get local IP
        var port = Port.GetValue(context);
        var htmlContent = HtmlContent.GetValue(context); // Get HTML content from input

        // Check if settings have changed and require a restart or content update
        var settingsChanged = shouldListen != _lastListenState || localIp != _lastLocalIp || port != _lastPort;

        if (settingsChanged)
        {
            StopServer();
            if (shouldListen)
                StartServer(localIp, port); // Pass localIp to StartServer

            _lastListenState = shouldListen;
            _lastLocalIp = localIp; // Store last local IP
            _lastPort = port;
        }

        // Always update the stored HTML content
        _lastHtmlContent = htmlContent;

        // Update output slots
        IsRunning.Value = _listener?.IsListening == true;
        PortOutput.Value = _lastPort;
    }
    #endregion
    #region Outputs
    [Output(Guid = "2A3B4C5D-6E7F-4890-1234-567890ABCDEF")] // Updated GUID
    public readonly Slot<bool> IsRunning = new();

    [Output(Guid = "3B4C5D6E-7F8A-4901-2345-67890ABCDEF1")] // Updated GUID
    public readonly Slot<int> PortOutput = new();
    #endregion

    #region State and Fields
    private HttpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _lastListenState;
    private string? _lastLocalIp; // Added for LocalIpAddress input
    private int _lastPort;
    private string? _lastHtmlContent; // Store last HTML content
    private bool _disposed;
    private bool _printToLog; // Added for PrintToLog functionality

    private string _statusMessage = "Not running";
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    #endregion

    #region Server Control Logic
    private void StartServer(string? localIpAddress, int port)
    {
        string prefixHost;

        // Determine the IP address to listen on
        if (string.IsNullOrEmpty(localIpAddress) || localIpAddress == "0.0.0.0 (Any)")
        {
            prefixHost = "+"; // Use '+' for HttpListener to listen on all interfaces
        }
        else if (!IPAddress.TryParse(localIpAddress, out _))
        {
            SetStatus($"Invalid Local IP '{localIpAddress}'. Defaulting to listen on all interfaces.", IStatusProvider.StatusLevel.Warning);
            if (_printToLog)
            {
                Log.Warning($"WebServer: Invalid Local IP '{localIpAddress}', defaulting to listen on all interfaces.", this);
            }

            prefixHost = "+";
        }
        else
        {
            prefixHost = localIpAddress; // Use the specific IP for the prefix
        }

        var prefix = $"http://{prefixHost}:{port}/";

        _listener = new HttpListener();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            SetStatus($"Running on {prefix.Replace("+", "localhost")}", IStatusProvider.StatusLevel.Success);
            if (_printToLog)
            {
                Log.Debug($"WebServer started on {prefix.Replace("+", "localhost")}", this);
            }

            // Start the listening loop on a background task
            _ = Task.Run(ListenLoop, _cancellationTokenSource.Token);
        }
        catch (HttpListenerException hlex) when (hlex.ErrorCode == 5) // Access Denied
        {
            SetStatus($"Access Denied on port {port}. Try port >1024 or run as admin. Error: {hlex.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog)
            {
                Log.Error($"WebServer: Access Denied starting WebServer on {prefix}: {hlex.Message}", this);
            }

            CleanupListener();
        }
        catch (Exception e)
        {
            SetStatus($"Failed to start: {e.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog)
            {
                Log.Error($"WebServer: Error starting WebServer: {e.Message}", this);
            }

            CleanupListener();
        }
    }

    private async Task ListenLoop()
    {
        try
        {
            while (_listener?.IsListening == true && !_cancellationTokenSource!.Token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (ObjectDisposedException)
                {
                    if (_printToLog) Log.Debug("WebServer: Listener disposed, exiting ListenLoop.", this);
                    break; // Listener was disposed, exiting the loop gracefully
                }
                catch (HttpListenerException hex) when
                    (hex.ErrorCode == 995) // The I/O operation has been aborted because of either a thread exit or an application request.
                {
                    if (_printToLog) Log.Debug("WebServer: HttpListenerException 995 (Operation Aborted), likely during shutdown. Exiting ListenLoop.", this);
                    break;
                }
                catch (OperationCanceledException)
                {
                    if (_printToLog) Log.Debug("WebServer: ListenLoop cancelled via token. Exiting.", this);
                    break;
                }
                catch (Exception ex)
                {
                    if (_printToLog) Log.Warning($"WebServer: Error getting request context: {ex.Message}", this);
                    continue;
                }

                // Handle the request asynchronously
                // Detach from the current task to allow the loop to continue accepting connections
                _ = Task.Run(() => HandleRequestAsync(context), _cancellationTokenSource.Token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException)
        {
            // Expected during shutdown
            if (_printToLog) Log.Debug($"WebServer: ListenLoop stopped gracefully due to {e.GetType().Name}.", this);
        }
        catch (Exception e)
        {
            if (_printToLog) Log.Warning($"WebServer: Unexpected error in ListenLoop: {e.Message}", this);
        }
        finally
        {
            if (_printToLog) Log.Debug("WebServer: ListenLoop finished.", this);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        var urlPath = "/"; // Initialize to a non-null default
        try
        {
            if (_printToLog) Log.Debug($"WebServer received request for '{request.Url?.AbsolutePath}' from {request.RemoteEndPoint}", this);

            // FIX for CS8600: Explicitly handle null for request.Url before accessing AbsolutePath
            if (request.Url != null)
            {
                urlPath = request.Url.AbsolutePath; // Assign after confirming request.Url is not null
            }

            // Serve the HTML content for the root path
            if (string.IsNullOrEmpty(urlPath) || urlPath == "/")
            {
                // Get the current HTML content from the stored variable
                var htmlToServe = _lastHtmlContent ?? "<html><body><h1>No HTML content provided.</h1></body></html>";

                byte[] buffer = Encoding.UTF8.GetBytes(htmlToServe);
                response.ContentType = "text/html";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = (int)HttpStatusCode.OK;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                if (_printToLog) Log.Debug("WebServer served HTML content.", this);
            }
            else
            {
                // For any other path, return 404
                if (_printToLog) Log.Debug($"WebServer: Path not found: '{urlPath}'", this);
                SendErrorResponse(response, 404, "Not Found");
            }
        }
        catch (Exception ex)
        {
            if (_printToLog) Log.Error($"WebServer: Error handling request for '{request.Url?.AbsolutePath}': {ex.Message}", this);
            SendErrorResponse(response, 500, "Internal Server Error");
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex)
            {
                if (_printToLog) Log.Debug($"WebServer: Error closing response: {ex.Message}", this);
            }
        }
    }

    private void SendErrorResponse(HttpListenerResponse response, int statusCode, string description)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes($"<html><body><h1>{statusCode} {description}</h1></body></html>");
            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = statusCode;

            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close(); // Ensure the stream is closed after writing
        }
        catch (Exception ex)
        {
            if (_printToLog) Log.Debug($"WebServer: Error sending error response ({statusCode}): {ex.Message}", this);
        }
    }

    private void StopServer()
    {
        try
        {
            _cancellationTokenSource?.Cancel(); // Signal cancellation
            if (_printToLog) Log.Debug("WebServer: Cancellation requested for server.", this);
        }
        catch (Exception ex)
        {
            if (_printToLog) Log.Debug($"WebServer: Error during cancellation: {ex.Message}", this);
        }

        CleanupListener(); // Stop and close HttpListener

        if (!_lastListenState)
            SetStatus("Not running", IStatusProvider.StatusLevel.Notice);
        else if (_printToLog)
        {
            Log.Debug("WebServer: Server stopped.", this);
        }

        IsRunning.DirtyFlag.Invalidate();
        PortOutput.DirtyFlag.Invalidate();
    }

    private void CleanupListener()
    {
        try
        {
            if (_listener != null)
            {
                if (_listener.IsListening)
                {
                    _listener.Stop();
                    if (_printToLog) Log.Debug("WebServer: HttpListener stopped.", this);
                }

                _listener.Close();
                if (_printToLog) Log.Debug("WebServer: HttpListener closed.", this);
                _listener = null;
            }
        }
        catch (Exception ex)
        {
            if (_printToLog) Log.Debug($"WebServer: Error stopping/closing HttpListener: {ex.Message}", this);
        }
    }
    #endregion

    #region IStatusProvider Implementation
    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string GetStatusMessage() => _statusMessage;

    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        _statusMessage = message;
        _statusLevel = level;
    }
    #endregion

    #region ICustomDropdownHolder Implementation
    string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
    {
        return id == LocalIpAddress.Id ? GetLocalIPv4Addresses() : Empty<string>();
    }

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

    #region Input Slots
    [Input(Guid = "4C5D6E7F-8A9B-4012-3456-7890ABCDEF12")] // Updated GUID
    public readonly InputSlot<bool> Listen = new();

    [Input(Guid = "5D6E7F8A-9B0C-4123-4567-890ABCDEF123")] // Updated GUID
    public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)"); // New input slot

    [Input(Guid = "6E7F8A9B-0C1D-4234-5678-90ABCDEF1234")] // Updated GUID
    public readonly InputSlot<int> Port = new(8080);

    /// <summary>
    /// The HTML content to serve when the root path (/) is requested.
    /// </summary>
    [Input(Guid = "7F8A9B0C-1D2E-4345-6789-0ABCDEF12345")] // Updated GUID
    public readonly InputSlot<string> HtmlContent = new(@"<!DOCTYPE html>
<html>
<head>
    <title>Default T3 WebServer Page</title>
</head>
<body>
    <h1>Welcome to the T3 WebServer</h1>
    <p>Provide your HTML content via the 'HtmlContent' input slot.</p>
    <p>Example interaction with a T3 WebSocket:</p>
    <div>
        <label for='slider1'>Slider 1:</label>
        <input type='range' id='slider1' min='0' max='100' value='50'>
        <span id='value1'>50</span>
    </div>
    <div>
        <button id='button1'>Send Message</button>
    </div>
    <div id='status'>Connecting...</div>
    <script>
        // --- Basic WebSocket Interaction Example ---
        // Make sure to change the port to match your T3 WebSocketServer port
        const WS_PORT = 8081;
        let ws;
        const statusEl = document.getElementById('status');
        const slider1 = document.getElementById('slider1');
        const value1 = document.getElementById('value1');
        const button1 = document.getElementById('button1');

        function connect() {
            ws = new WebSocket(`ws://localhost:${WS_PORT}`);
            ws.onopen = () => {
                statusEl.textContent = 'Connected';
                statusEl.style.color = 'green';
            };
            ws.onmessage = (event) => {
                console.log('Received:', event.data);
                statusEl.textContent = `Received: ${event.data}`;
                statusEl.style.color = 'blue';
                // Example: Update slider if T3 sends 'SET_SLIDER1:75'
                if (event.data.startsWith('SET_SLIDER1:')) {
                     const val = event.data.split(':')[1];
                     slider1.value = val;
                     value1.textContent = val;
                }
            };
            ws.onclose = () => {
                statusEl.textContent = 'Disconnected. Reconnecting...';
                statusEl.style.color = 'orange';
                setTimeout(connect, 3000);
            };
            ws.onerror = (err) => {
                statusEl.textContent = 'Error';
                statusEl.style.color = 'red';
                console.error('WebSocket error:', err);
            };
        }

        slider1.addEventListener('input', function() {
            value1.textContent = slider1.value;
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(`SLIDER1:${slider1.value}`);
            }
        });

        button1.addEventListener('click', function() {
             if (ws && ws.readyState === WebSocket.OPEN) {
                 ws.send('BUTTON1_CLICKED');
             }
        });

        window.addEventListener('load', connect);
    </script>
</body>
</html>");

    [Input(Guid = "8A9B0C1D-2E3F-4456-7890-ABCDEF123456")] // New GUID for PrintToLog
    public readonly InputSlot<bool> PrintToLog = new();
    #endregion
}