#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using T3.Core.Utils;

namespace Lib.io.udp;

[Guid("C029B846-B442-458B-933B-653609827B75")]
internal sealed class UDPInput : Instance<UDPInput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    [Output(Guid = "21E92723-E786-4556-91F6-31804301509D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> ReceivedString = new();
    [Output(Guid = "444498A6-972F-4375-A152-A103AC537A1D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<string>> ReceivedLines = new();
    [Output(Guid = "8056024D-4581-4328-8547-19B44EA58742", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> LastSenderAddress = new();
    [Output(Guid = "D938634B-3736-444F-942F-C2D046D06D4D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> LastSenderPort = new();
    [Output(Guid = "E4162B57-5586-4513-A551-7C64B95B8A1D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasTrigger = new();
    [Output(Guid = "9C4D3558-1584-422E-A59B-D08D23E45242")]
    public readonly Slot<bool> IsListening = new();

    public UDPInput()
    {
        ReceivedString.UpdateAction = Update;
        ReceivedLines.UpdateAction = Update;
        LastSenderAddress.UpdateAction = Update;
        LastSenderPort.UpdateAction = Update;
        WasTrigger.UpdateAction = Update;
        IsListening.UpdateAction = Update;
    }

    private bool _wasListening;
    private string? _lastLocalIp;
    private int _lastPort;
    private bool _printToLog; // ONLY ONE DECLARATION OF _printToLog

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);
        var shouldListen = Listen.GetValue(context);
        var localIp = LocalIpAddress.GetValue(context);
        var port = Port.GetValue(context);

        var settingsChanged = shouldListen != _wasListening || localIp != _lastLocalIp || port != _lastPort;
        if (settingsChanged)
        {
            StopListening();
            if (shouldListen) StartListening();
            _wasListening = shouldListen;
            _lastLocalIp = localIp;
            _lastPort = port;
        }

        var listLength = ListLength.GetValue(context).Clamp(1, 1000);
        var wasTriggered = false;
        while (_receivedQueue.TryDequeue(out var msg))
        {
            ReceivedString.Value = msg.DecodedString;
            LastSenderAddress.Value = msg.Source.Address.ToString();
            LastSenderPort.Value = msg.Source.Port;
            _messageHistory.Add(msg.DecodedString);
            wasTriggered = true;
        }
        while (_messageHistory.Count > listLength) { _messageHistory.RemoveAt(0); }
        ReceivedLines.Value = _messageHistory;
        WasTrigger.Value = wasTriggered;
        IsListening.Value = _runListener;

        UpdateStatusMessage();
    }

    private void StartListening()
    {
        if (_listenerThread is { IsAlive: true }) return;
        _runListener = true;
        _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "UDPInputListener" };
        _listenerThread.Start();
        if (_printToLog)
        {
            Log.Debug($"UDP Input: Starting listener thread.", this);
        }
    }

    private void StopListening()
    {
        if (!_runListener) return;
        _runListener = false;
        if (_printToLog)
        {
            Log.Debug("UDP Input: Stopping listener.", this);
        }
        _udpClient?.Close();
        _listenerThread?.Join(200); // Give the thread a moment to shut down
        _listenerThread = null;
        if (_printToLog)
        {
            Log.Debug("UDP Input: Listener stopped.", this);
        }
    }

    private void ListenLoop()
    {
        var port = Port.Value;
        var localIpStr = LocalIpAddress.Value;
        UdpClient? currentUdpClient = null; // Declare locally

        try
        {
            var listenIp = IPAddress.Any;
            if (!string.IsNullOrEmpty(localIpStr) && localIpStr != "0.0.0.0 (Any)" && IPAddress.TryParse(localIpStr, out var parsedIp))
                listenIp = parsedIp;
            var localEp = new IPEndPoint(listenIp, port);

            currentUdpClient = new UdpClient { ExclusiveAddressUse = false };
            currentUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            currentUdpClient.Client.Bind(localEp);

            _udpClient = currentUdpClient;

            if (_printToLog)
            {
                Log.Debug($"UDP Input: Bound to {localEp.Address}:{localEp.Port}. Ready to receive.", this);
            }

            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            while (_runListener)
            {
                try
                {
                    if (_udpClient == null) break;
                    var data = _udpClient.Receive(ref remoteEP);
                    if (_printToLog)
                    {
                        var message = Encoding.UTF8.GetString(data);
                        Log.Debug($"UDP Input received from {remoteEP.Address}:{remoteEP.Port}: \"{message}\"", this);
                    }
                    _receivedQueue.Enqueue(new ReceivedMessage(data, remoteEP));
                    ReceivedString.DirtyFlag.Invalidate();
                }
                catch (SocketException ex)
                {
                    if (_runListener)
                    {
                        Log.Warning($"UDP Input receive socket error: {ex.Message} (Error Code: {ex.ErrorCode})", this);
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (_runListener)
                    {
                        Log.Error($"UDP Input receive error: {e.Message}", this);
                    }
                }
            }
        }
        catch (Exception e)
        {
            SetStatus($"Failed to bind to {localIpStr}:{port}. Error: {e.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog)
            {
                Log.Error($"UDP Input: Failed to bind to {localIpStr}:{port}. Error: {e.Message}", this);
            }
        }
        finally
        {
            currentUdpClient?.Close();
            if (_udpClient == currentUdpClient) _udpClient = null;
        }
    }

    private void UpdateStatusMessage()
    {
        var localIpDisplay = LocalIpAddress.Value;
        if (!_runListener) SetStatus("Not listening. Enable 'Listen'.", IStatusProvider.StatusLevel.Notice);
        else if (_lastStatusLevel != IStatusProvider.StatusLevel.Error)
            SetStatus($"Listening on {localIpDisplay.Split(' ')[0]}:{Port.Value}", IStatusProvider.StatusLevel.Success);
    }

    public void Dispose() { StopListening(); }

    #region internal types, state, and providers
    private UdpClient? _udpClient;
    private Thread? _listenerThread;
    private volatile bool _runListener;
    // REMOVED DUPLICATE: private bool _printToLog; 
    private readonly ConcurrentQueue<ReceivedMessage> _receivedQueue = new();
    private readonly List<string> _messageHistory = new();
    private readonly struct ReceivedMessage
    {
        public readonly IPEndPoint Source;
        public readonly string DecodedString;
        public ReceivedMessage(byte[] data, IPEndPoint source) { Source = source; DecodedString = Encoding.UTF8.GetString(data); }
    }
    private string? _lastErrorMessage = "Not listening.";
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _lastStatusLevel = l; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
    public string? GetStatusMessage() => _lastErrorMessage;
    string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;
    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id) => id == LocalIpAddress.Id ? GetLocalIPv4Addresses() : Enumerable.Empty<string>();
    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? s, bool i)
    {
        if (string.IsNullOrEmpty(s) || !i || id != LocalIpAddress.Id) return;
        LocalIpAddress.SetTypedInputValue(s.Split(' ')[0]);
    }
    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        yield return "0.0.0.0 (Any)";
        yield return "127.0.0.1";
        if (!NetworkInterface.GetIsNetworkAvailable()) yield break;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return ipInfo.Address.ToString();
        }
    }
    #endregion

    [Input(Guid = "2EBE418D-407E-46D8-B274-13B41C52ACCF")] public readonly InputSlot<int> Port = new(7000);
    [Input(Guid = "9E23335A-D63A-4286-930E-C63E86D0E6F0")] public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)");
    [Input(Guid = "E6589335-4E51-41B0-8777-6A5D54C4F0EE")] public readonly InputSlot<int> ListLength = new(10);
    [Input(Guid = "0944714D-693D-4251-93A6-E22A2DB64F20")] public readonly InputSlot<bool> Listen = new();
    [Input(Guid = "5E725916-4143-4759-8651-E12185C658D3")] public readonly InputSlot<bool> PrintToLog = new();
}