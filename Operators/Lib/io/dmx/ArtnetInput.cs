#nullable enable
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using T3.Core.Utils;

namespace Lib.io.dmx;

[Guid("fc03dcd0-6f2f-4507-be06-1ed105607489")]
internal sealed class ArtnetInput : Instance<ArtnetInput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private const int ArtNetPort = 6454;
    private static readonly byte[] ArtnetId = "Art-Net\0"u8.ToArray();

    [Output(Guid = "d3c09c87-c508-4621-a54d-f14d85c3f75f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<int>> Result = new();

    public ArtnetInput()
    {
        Result.UpdateAction = Update;
    }

    private string? _lastLocalIp;
    private bool _wasActive;
    private bool _printToLog; // Added for PrintToLog functionality

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context); // Update printToLog flag
        var active = Active.GetValue(context);
        var localIp = LocalIpAddress.GetValue(context);

        var settingsChanged = active != _wasActive || localIp != _lastLocalIp;
        if (settingsChanged)
        {
            StopListening();
            if (active) StartListening();
            _wasActive = active;
            _lastLocalIp = localIp;
        }

        CleanupStaleUniverses(Timeout.GetValue(context));

        var startUniverse = StartUniverse.GetValue(context);
        var numUniverses = NumUniverses.GetValue(context).Clamp(1, 4096);
        var combinedDmxData = new List<int>(numUniverses * 512);

        for (var i = 0; i < numUniverses; i++)
        {
            var currentUniverseId = startUniverse + i;
            if (_receivedUniverses.TryGetValue(currentUniverseId, out var universeData))
            {
                var dmxSnapshot = new int[512];
                lock (universeData.DmxData)
                {
                    for (var j = 0; j < 512; ++j) dmxSnapshot[j] = universeData.DmxData[j];
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
        _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "ArtNetInputListener" };
        _listenerThread.Start();
        if (_printToLog)
        {
            Log.Debug($"Artnet Input: Starting listener thread.", this);
        }
    }

    private void StopListening()
    {
        if (!_runListener) return;
        _runListener = false;
        if (_printToLog)
        {
            Log.Debug("Artnet Input: Stopping listener.", this);
        }
        _udpClient?.Close(); // This will unblock the Receive call in ListenLoop
        _listenerThread?.Join(200); // Give the thread a moment to shut down
        _listenerThread = null;
        if (_printToLog)
        {
            Log.Debug("Artnet Input: Listener stopped.", this);
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

            currentUdpClient = new UdpClient { ExclusiveAddressUse = false };
            currentUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            currentUdpClient.Client.Bind(new IPEndPoint(listenIp, ArtNetPort));

            _udpClient = currentUdpClient; // Assign to member field after successful bind

            if (_printToLog)
            {
                Log.Debug($"Artnet Input: Bound to {listenIp}:{ArtNetPort}. Ready to receive.", this);
            }

            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (_runListener)
            {
                try
                {
                    if (_udpClient == null) break; // Check if client was disposed externally
                    var data = _udpClient.Receive(ref remoteEndPoint);

                    if (data.Length < 18 || !data.AsSpan(0, 8).SequenceEqual(ArtnetId) || data[8] != 0x00 || data[9] != 0x50) continue;

                    var universe = data[14] | (data[15] << 8);
                    var length = (data[16] << 8) | data[17];
                    if (length == 0 || length > 512 || data.Length < 18 + length) continue;

                    var universeData = _receivedUniverses.GetOrAdd(universe, _ => new UniverseData());
                    lock (universeData.DmxData)
                    {
                        System.Buffer.BlockCopy(data, 18, universeData.DmxData, 0, length);
                        if (length < 512) Array.Clear(universeData.DmxData, length, 512 - length);
                    }
                    universeData.LastReceivedTicks = Stopwatch.GetTimestamp();
                    Result.DirtyFlag.Invalidate();

                    if (_printToLog)
                    {
                        Log.Debug($"Artnet Input: Received Art-Net DMX for Universe {universe} from {remoteEndPoint.Address}:{remoteEndPoint.Port}", this);
                    }
                }
                catch (SocketException ex)
                {
                    if (_runListener) // Only log if not intentionally stopping
                    {
                        Log.Warning($"Artnet Input receive socket error: {ex.Message} (Error Code: {ex.ErrorCode})", this);
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (_runListener)
                    {
                        Log.Error($"Art-Net receive error: {e.Message}", this);
                    }
                }
            }
        }
        catch (Exception e)
        {
            SetStatus($"Failed to bind to port {ArtNetPort}: {e.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog)
            {
                Log.Error($"Artnet Input: Failed to bind to port {ArtNetPort}: {e.Message}", this);
            }
        }
        finally
        {
            currentUdpClient?.Close();
            if (_udpClient == currentUdpClient) _udpClient = null; // Clear if it's the one we set
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
                    Log.Debug($"Artnet Input: Universe {universeId} timed out and removed.", this);
                }
            }
        }
    }

    private void UpdateStatusMessage(int numUniverses, int startUniverse)
    {
        var localIpDisplay = LocalIpAddress.Value;
        if (!_wasActive)
        {
            SetStatus("Inactive. Enable 'Active'.", IStatusProvider.StatusLevel.Notice);
        }
        else if (_lastStatusLevel != IStatusProvider.StatusLevel.Error)
        {
            var receivedCount = _receivedUniverses.Count;
            if (receivedCount == 0)
            {
                SetStatus($"Listening on {localIpDisplay.Split(' ')[0]}:{ArtNetPort} for {numUniverses} universes (from {startUniverse})... No packets received.", IStatusProvider.StatusLevel.Warning);
            }
            else
            {
                SetStatus($"Listening for {numUniverses} universes. Receiving {receivedCount} active universes.", IStatusProvider.StatusLevel.Success);
            }
        }
    }

    public void Dispose() { StopListening(); }

    private sealed class UniverseData { public readonly byte[] DmxData = new byte[512]; public long LastReceivedTicks; }
    private Thread? _listenerThread;
    private volatile bool _runListener;
    private UdpClient? _udpClient;
    private readonly ConcurrentDictionary<int, UniverseData> _receivedUniverses = new();

    #region IStatusProvider & ICustomDropdownHolder
    private string _lastStatusMessage = "Inactive";
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastStatusMessage = m; _lastStatusLevel = l; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
    public string GetStatusMessage() => _lastStatusMessage;

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

    // Unique GUID for LocalIpAddress in ArtnetInput
    [Input(Guid = "24B5D450-4E83-49DB-88B1-7D688E64585D")] public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)");
    [Input(Guid = "3d085f6f-6f4a-4876-805f-22f25497a731")] public readonly InputSlot<bool> Active = new();
    [Input(Guid = "19bde769-3992-4cf0-a0b4-e3ae25c03c79")] public readonly InputSlot<int> StartUniverse = new(1);
    [Input(Guid = "c18a9359-3ef8-4e0d-85d8-51f725357388")] public readonly InputSlot<int> NumUniverses = new(1);
    [Input(Guid = "a38c29b6-057d-4883-9366-139366113b63")] public readonly InputSlot<float> Timeout = new(1.2f);
    // New InputSlot for PrintToLog
    [Input(Guid = "A5B6C7D8-E9F0-4123-4567-890ABCDEF123")] // New GUID
    public readonly InputSlot<bool> PrintToLog = new();
}