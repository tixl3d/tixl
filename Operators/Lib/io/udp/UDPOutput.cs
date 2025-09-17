#nullable enable
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lib.io.udp;

[Guid("34E4E63B-2708-4673-B682-1D07D0245E1E")]
internal sealed class UDPOutput : Instance<UDPOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    [Output(Guid = "0E2B808F-63A5-4927-9610-410E5F5227B1")] public readonly Slot<Command> Result = new();
    [Output(Guid = "07B57F3A-8993-4B9F-8349-D0A4762E4447")] public readonly Slot<bool> IsConnected = new();

    public UDPOutput() { Result.UpdateAction = Update; }

    private UdpClient? _udpClient;
    private string? _lastLocalIp;
    private bool _lastConnectState;
    private string? _lastSentMessage;
    private bool _printToLog; // Added for PrintToLog functionality

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context); // Update printToLog flag

        var localIp = LocalIpAddress.GetValue(context);
        var shouldConnect = Connect.GetValue(context);

        var settingsChanged = localIp != _lastLocalIp || shouldConnect != _lastConnectState;
        if (settingsChanged)
        {
            CloseSocket();
            if (shouldConnect) OpenSocket(localIp);
            _lastLocalIp = localIp;
            _lastConnectState = shouldConnect;
        }

        var isConnected = _udpClient != null;
        IsConnected.Value = isConnected;

        var separator = Separator.GetValue(context) ?? "";
        var messageParts = MessageParts.GetCollectedTypedInputs().Select(p => p.GetValue(context));
        var currentMessage = string.Join(separator, messageParts);
        var hasMessageChanged = currentMessage != _lastSentMessage;
        var manualTrigger = SendTrigger.GetValue(context);
        var sendOnChange = SendOnChange.GetValue(context);
        var shouldSend = manualTrigger || (sendOnChange && hasMessageChanged);

        if (isConnected && shouldSend)
        {
            if (manualTrigger) SendTrigger.SetTypedInputValue(false);
            var targetIp = TargetIpAddress.GetValue(context);
            var targetPort = TargetPort.GetValue(context);

            if (!string.IsNullOrEmpty(currentMessage) && !string.IsNullOrEmpty(targetIp) && targetPort > 0)
            {
                var data = Encoding.UTF8.GetBytes(currentMessage);
                try
                {
                    _udpClient!.Send(data, data.Length, targetIp, targetPort);
                    if (_printToLog)
                    {
                        Log.Debug($"UDP Output → '{currentMessage}' to {targetIp}:{targetPort}", this);
                    }
                }
                catch (Exception e)
                {
                    SetStatus($"UDP send error: {e.Message}", IStatusProvider.StatusLevel.Warning);
                    if (_printToLog)
                    {
                        Log.Warning($"UDP Output: Send error to {targetIp}:{targetPort}: {e.Message}", this);
                    }
                }
                _lastSentMessage = currentMessage;
            }
        }
    }

    private void OpenSocket(string? localIpAddress)
    {
        if (string.IsNullOrEmpty(localIpAddress)) { SetStatus("Local IP not selected.", IStatusProvider.StatusLevel.Warning); return; }
        if (!IPAddress.TryParse(localIpAddress, out var ip)) { SetStatus($"Invalid Local IP.", IStatusProvider.StatusLevel.Warning); return; }

        try
        {
            var localEndPoint = new IPEndPoint(ip, 0); // Bind to a dynamic port for sending
            _udpClient = new UdpClient(localEndPoint);
            SetStatus($"Socket ready on {localEndPoint}", IStatusProvider.StatusLevel.Success);
            if (_printToLog)
            {
                Log.Debug($"UDP Output: Socket opened on {localEndPoint}", this);
            }
        }
        catch (Exception e)
        {
            SetStatus($"Failed to open socket: {e.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog)
            {
                Log.Error($"UDP Output: Failed to open socket on {localIpAddress}: {e.Message}", this);
            }
            _udpClient?.Dispose();
            _udpClient = null;
        }
    }

    private void CloseSocket()
    {
        _udpClient?.Close();
        _udpClient = null;
        if (_lastConnectState)
        {
            SetStatus("Disconnected", IStatusProvider.StatusLevel.Notice);
            if (_printToLog)
            {
                Log.Debug("UDP Output: Socket closed.", this);
            }
        }
    }

    public void Dispose() { CloseSocket(); }

    private string? _lastErrorMessage = "Not connected.";
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

    [Input(Guid = "9E23335A-D63A-4286-930E-C63E86D0E6F0")] public readonly InputSlot<string> LocalIpAddress = new("127.0.0.1"); // Default updated
    [Input(Guid = "24B5D450-4E83-49DB-88B1-7D688E64585D")] public readonly InputSlot<string> TargetIpAddress = new("127.0.0.1");
    [Input(Guid = "36C2BF8B-3E0C-4856-AA4A-32943A4B0223")] public readonly InputSlot<int> TargetPort = new(7001);
    [Input(Guid = "59074D76-1F4F-406A-B512-5813F4E3420E")] public readonly MultiInputSlot<string> MessageParts = new();
    [Input(Guid = "82933C40-DA9E-4340-A227-E9BACF6E2063")] public readonly InputSlot<string> Separator = new(" ");
    [Input(Guid = "216A0356-EF4A-413A-A656-7497127E31D4")] public readonly InputSlot<bool> SendOnChange = new(true);
    [Input(Guid = "C7AC22C0-A31E-41F6-B29D-D40956E6688B")] public readonly InputSlot<bool> SendTrigger = new();
    [Input(Guid = "7AB8F2A6-4874-4235-85A5-D0E1F30C0446")] public readonly InputSlot<bool> Connect = new();
    [Input(Guid = "A1B2C3D4-E5F6-4789-89AB-CDEF01234567")] public readonly InputSlot<bool> PrintToLog = new(); // New GUID for PrintToLog
}