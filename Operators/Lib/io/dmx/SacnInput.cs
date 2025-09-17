#nullable enable
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using T3.Core.Utils;

namespace Lib.io.dmx;

[Guid("e3207424-deaf-4462-acd5-21f2c6f16d1b")]
internal sealed class SacnInput : Instance<SacnInput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private const int SacnPort = 5568;
    private static readonly byte[] SacnId = "ASC-E1.17\0\0\0"u8.ToArray();
    private const int MinPacketLength = 126;

    [Output(Guid = "b0bcc3de-de79-42ac-a9cc-ec5a699f252b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<int>> Result = new();

    public SacnInput()
    {
        Result.UpdateAction = Update;
    }

    private volatile bool _isListening; // Made volatile
    private string? _lastLocalIp;
    private bool _printToLog; // Added for PrintToLog functionality

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context); // Update printToLog flag
        var active = Active.GetValue(context);
        var localIp = LocalIpAddress.GetValue(context);
        var startUniverse = StartUniverse.GetValue(context);
        var numUniverses = NumUniverses.GetValue(context).Clamp(1, 4096);

        var settingsChanged = active != _isListening || localIp != _lastLocalIp;
        if (settingsChanged)
        {
            _isListening = active; // Update _isListening state
            StopListening();
            if (active) StartListening();
            _lastLocalIp = localIp;
        }

        if (_isListening)
        {
            UpdateUniverseSubscriptions(startUniverse, numUniverses);
        }

        CleanupStaleUniverses(Timeout.GetValue(context));

        var combinedDmxData = new List<int>(numUniverses * 512);
        for (var i = 0; i < numUniverses; i++)
        {
            var currentUniverseId = startUniverse + i;
            if (_receivedUniverses.TryGetValue(currentUniverseId, out var universeData))
            {
                var dmxSnapshot = new int[512];
                lock (universeData.DmxData)
                {
                    for (var j = 0; j < 512; j++) dmxSnapshot[j] = universeData.DmxData[j];
                }
                combinedDmxData.AddRange(dmxSnapshot);
            }
            else
            {
                combinedDmxData.AddRange(Enumerable.Repeat(0, 512));
            }
        }
        Result.Value = combinedDmxData;
        UpdateStatusMessage(numUniverses, startUniverse);
    }

    private void StartListening()
    {
        if (_listenerThread is { IsAlive: true }) return;
        _runListener = true;
        _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "SacnInputListener" };
        _listenerThread.Start();
        if (_printToLog)
        {
            Log.Debug($"sACN Input: Starting listener thread.", this);
        }
    }

    private void StopListening()
    {
        if (!_runListener) return;
        _runListener = false;
        if (_printToLog)
        {
            Log.Debug("sACN Input: Stopping listener.", this);
        }
        _udpClient?.Close(); // This will unblock the Receive call in ListenLoop
        _listenerThread?.Join(200); // Give the thread a moment to shut down
        _listenerThread = null;
        _subscribedUniverses.Clear();
        if (_printToLog)
        {
            Log.Debug("sACN Input: Listener stopped.", this);
        }
    }

    private void ListenLoop()
    {
        UdpClient? currentUdpClient = null; // Declare locally for safer cleanup
        try
        {
            var localIpStr = LocalIpAddress.Value;
            var listenIp = IPAddress.Any;
            if (!string.IsNullOrEmpty(localIpStr) && localIpStr != "0.0.0.0 (Any)" && IPAddress.TryParse(localIpStr, out var parsedIp))
                listenIp = parsedIp;
            _boundIpAddress = listenIp;

            currentUdpClient = new UdpClient { ExclusiveAddressUse = false };
            currentUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            currentUdpClient.Client.Bind(new IPEndPoint(_boundIpAddress, SacnPort));

            _udpClient = currentUdpClient; // Assign to member field after successful bind

            if (_printToLog)
            {
                Log.Debug($"sACN Input: Bound to {_boundIpAddress}:{SacnPort}. Ready to receive.", this);
            }

            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0); // Correct variable name
            while (_runListener)
            {
                try
                {
                    if (_udpClient == null) break; // Check if client was disposed externally
                    var data = _udpClient.Receive(ref remoteEndPoint);
                    if (data.Length < MinPacketLength || !data.AsSpan(4, 12).SequenceEqual(SacnId)) continue;

                    var universe = (data[113] << 8) | data[114];
                    var propertyValueCount = (data[123] << 8) | data[124];
                    var dmxLength = propertyValueCount - 1; // Subtract 1 for the start code
                    if (dmxLength <= 0 || dmxLength > 512 || data.Length < 126 + dmxLength) continue;

                    var universeData = _receivedUniverses.GetOrAdd(universe, _ => new UniverseData());
                    lock (universeData.DmxData)
                    {
                        System.Buffer.BlockCopy(data, 126, universeData.DmxData, 0, dmxLength);
                        if (dmxLength < 512) Array.Clear(universeData.DmxData, dmxLength, 512 - dmxLength);
                    }
                    universeData.LastReceivedTicks = Stopwatch.GetTimestamp();
                    Result.DirtyFlag.Invalidate();

                    if (_printToLog)
                    {
                        Log.Debug($"sACN Input: Received sACN DMX for Universe {universe} from {remoteEndPoint.Address}:{remoteEndPoint.Port}", this); // Corrected here
                    }
                }
                catch (SocketException ex)
                {
                    if (_runListener) // Only log if not intentionally stopping
                    {
                        Log.Warning($"sACN Input receive socket error: {ex.Message} (Error Code: {ex.ErrorCode})", this);
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (_runListener)
                    {
                        Log.Error($"sACN receive error: {e.Message}", this);
                    }
                }
            }
        }
        catch (Exception e)
        {
            SetStatus($"Failed to bind sACN socket: {e.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog)
            {
                Log.Error($"sACN Input: Failed to bind sACN socket to {_boundIpAddress}:{SacnPort}: {e.Message}", this);
            }
        }
        finally
        {
            currentUdpClient?.Close();
            if (_udpClient == currentUdpClient) _udpClient = null; // Clear if it's the one we set
        }
    }

    private void UpdateUniverseSubscriptions(int startUniverse, int numUniverses)
    {
        if (!_runListener || _udpClient == null || _boundIpAddress == null) return;
        var requiredUniverses = new HashSet<int>(Enumerable.Range(startUniverse, numUniverses));
        var universesToUnsubscribe = _subscribedUniverses.Except(requiredUniverses).ToList();
        var universesToSubscribe = requiredUniverses.Except(_subscribedUniverses).ToList();

        foreach (var uni in universesToUnsubscribe)
        {
            try
            {
                _udpClient.DropMulticastGroup(GetSacnMulticastAddress(uni));
                _subscribedUniverses.Remove(uni);
                if (_printToLog)
                {
                    Log.Debug($"sACN Input: Unsubscribed from multicast group for Universe {uni}.", this);
                }
            }
            catch (Exception e) { Log.Warning($"sACN Input: Failed to unsubscribe from sACN universe {uni}: {e.Message}", this); }
        }
        foreach (var uni in universesToSubscribe)
        {
            try
            {
                _udpClient.JoinMulticastGroup(GetSacnMulticastAddress(uni), _boundIpAddress);
                _subscribedUniverses.Add(uni);
                if (_printToLog)
                {
                    Log.Debug($"sACN Input: Subscribed to multicast group for Universe {uni}.", this);
                }
            }
            catch (Exception e) { Log.Warning($"sACN Input: Failed to subscribe to sACN universe {uni}: {e.Message}", this); }
        }
    }

    private void CleanupStaleUniverses(float timeoutInSeconds)
    {
        if (timeoutInSeconds <= 0) return;
        var timeoutTicks = (long)(timeoutInSeconds * Stopwatch.Frequency);
        var nowTicks = Stopwatch.GetTimestamp();
        var staleUniverses = _receivedUniverses.Where(pair => (nowTicks - pair.Value.LastReceivedTicks) > timeoutTicks).Select(pair => pair.Key).ToList();
        foreach (var universeId in staleUniverses)
        {
            if (_receivedUniverses.TryRemove(universeId, out _))
            {
                Result.DirtyFlag.Invalidate();
                if (_printToLog)
                {
                    Log.Debug($"sACN Input: Universe {universeId} timed out and removed.", this);
                }
            }
        }
    }

    private void UpdateStatusMessage(int numUniverses, int startUniverse)
    {
        var localIpDisplay = LocalIpAddress.Value;
        if (!_isListening) SetStatus("Inactive. Enable 'Active'.", IStatusProvider.StatusLevel.Notice);
        else if (_lastStatusLevel != IStatusProvider.StatusLevel.Error)
        {
            var receivedCount = _receivedUniverses.Count;
            if (receivedCount == 0) SetStatus($"Listening on {localIpDisplay.Split(' ')[0]}:{SacnPort} for {numUniverses} universes (from {startUniverse})... No packets received.", IStatusProvider.StatusLevel.Warning);
            else SetStatus($"Listening for {numUniverses} universes. Receiving {receivedCount} active universes.", IStatusProvider.StatusLevel.Success);
        }
    }

    private static IPAddress GetSacnMulticastAddress(int universe)
    {
        var highByte = (byte)(universe >> 8);
        var lowByte = (byte)(universe & 0xFF);
        return new IPAddress(new byte[] { 239, 255, highByte, lowByte });
    }

    public void Dispose() { StopListening(); }

    private sealed class UniverseData { public readonly byte[] DmxData = new byte[512]; public long LastReceivedTicks; }
    private Thread? _listenerThread;
    private volatile bool _runListener;
    private IPAddress? _boundIpAddress;
    private UdpClient? _udpClient;
    private readonly ConcurrentDictionary<int, UniverseData> _receivedUniverses = new();
    private readonly HashSet<int> _subscribedUniverses = new();

    #region IStatusProvider & ICustomDropdownHolder
    private string _lastStatusMessage = "Inactive."; // Default status on init
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string message, IStatusProvider.StatusLevel level) { _lastStatusMessage = message; _lastStatusLevel = level; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
    public string GetStatusMessage() => _lastStatusMessage;

    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => inputId == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;
    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => inputId == LocalIpAddress.Id ? GetLocalIPv4Addresses() : Enumerable.Empty<string>();

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem || inputId != LocalIpAddress.Id) return;
        var ip = selected.Split(' ')[0];
        LocalIpAddress.SetTypedInputValue(ip);
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

    // Unique GUID for LocalIpAddress in SacnInput
    [Input(Guid = "24B5D450-4E83-49DB-88B1-7D688E64585D")] public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)");
    [Input(Guid = "ca55c1b3-0669-46f1-bcc4-ee2e7f5a6028")] public readonly InputSlot<bool> Active = new();
    [Input(Guid = "0c348760-474e-4e30-a8c1-55e59cb1a908")] public readonly InputSlot<int> StartUniverse = new(1);
    [Input(Guid = "2cffbf1c-ce09-4283-a685-5234e4e49fee")] public readonly InputSlot<int> NumUniverses = new(1);
    [Input(Guid = "bed01653-6cd0-4578-81a9-3eda144ab279")] public readonly InputSlot<float> Timeout = new();
    // New InputSlot for PrintToLog
    [Input(Guid = "C3D4E5F6-A7B8-4901-2345-67890ABCDEF1")] // New GUID
    public readonly InputSlot<bool> PrintToLog = new();
}