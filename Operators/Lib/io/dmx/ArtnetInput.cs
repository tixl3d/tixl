#nullable enable
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using T3.Core.Utils;
using T3.Core.Logging;

namespace Lib.io.dmx;

[Guid("fc03dcd0-6f2f-4507-be06-1ed105607489")]
internal sealed class ArtnetInput : Instance<ArtnetInput>, IStatusProvider, ICustomDropdownHolder
{
    private const int ArtNetPort = 6454;
    private static readonly byte[] _artnetId = "Art-Net\0"u8.ToArray();
    private readonly ConcurrentDictionary<int, UniverseData> _receivedUniverses = new();

    [Input(Guid = "3d085f6f-6f4a-4876-805f-22f25497a731")]
    public readonly InputSlot<bool> Active = new();

    [Input(Guid = "24B5D450-4E83-49DB-88B1-7D688E64585D")]
    public readonly InputSlot<string> LocalIpAddress = new("0.0.0.0 (Any)");

    [Input(Guid = "c18a9359-3ef8-4e0d-85d8-51f725357388")]
    public readonly InputSlot<int> NumUniverses = new(1);

    [Input(Guid = "A5B6C7D8-E9F0-4123-4567-890ABCDEF123")]
    public readonly InputSlot<bool> PrintToLog = new();

    [Output(Guid = "d3c09c87-c508-4621-a54d-f14d85c3f75f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<int>> Result = new();

    [Input(Guid = "19bde769-3992-4cf0-a0b4-e3ae25c03c79")]
    public readonly InputSlot<int> StartUniverse = new(1);

    [Input(Guid = "a38c29b6-057d-4883-9366-139366113b63")]
    public readonly InputSlot<float> Timeout = new(1.2f);

    private string? _lastLocalIp;

    private Thread? _listenerThread;
    private bool _printToLog;
    private volatile bool _runListener;
    private UdpClient? _udpClient;
    private bool _wasActive;
    private double _lastRetryTime;

    public ArtnetInput()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);
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
        else if (active && (_listenerThread == null || !_listenerThread.IsAlive))
        {
            if (context.LocalTime - _lastRetryTime > 2.0)
            {
                _lastRetryTime = context.LocalTime;
                StartListening();
            }
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
                combinedDmxData.AddRange(Repeat(0, 512));
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

        _udpClient?.Close();
        _listenerThread?.Join(200);
        _listenerThread = null;
        if (_printToLog)
        {
            Log.Debug("Artnet Input: Listener stopped.", this);
        }
    }

    private void ListenLoop()
    {
        UdpClient? currentUdpClient = null;
        try
        {
            var localIpStr = LocalIpAddress.Value;
            var listenIp = IPAddress.Any;
            if (!string.IsNullOrEmpty(localIpStr) && localIpStr != "0.0.0.0 (Any)" && IPAddress.TryParse(localIpStr, out var parsedIp))
                listenIp = parsedIp;

            currentUdpClient = new UdpClient { ExclusiveAddressUse = false };
            currentUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            currentUdpClient.Client.Bind(new IPEndPoint(listenIp, ArtNetPort));

            _udpClient = currentUdpClient;

            if (_printToLog)
            {
                Log.Debug($"Artnet Input: Bound to {listenIp}:{ArtNetPort}. Ready to receive.", this);
            }

            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (_runListener)
            {
                try
                {
                    if (_udpClient == null) break;
                    var data = _udpClient.Receive(ref remoteEndPoint);

                    if (data.Length < 18 || !data.AsSpan(0, 8).SequenceEqual(_artnetId) || data[8] != 0x00 || data[9] != 0x50) continue;

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
                    if (_runListener)
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
            if (_udpClient == currentUdpClient) _udpClient = null;
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
        var localIpDisplay = LocalIpAddress.Value ?? string.Empty;
        if (!_wasActive)
        {
            SetStatus("Inactive. Enable 'Active'.", IStatusProvider.StatusLevel.Notice);
        }
        else if (_lastStatusLevel != IStatusProvider.StatusLevel.Error)
        {
            var receivedCount = _receivedUniverses.Count;
            if (receivedCount == 0)
            {
                SetStatus($"Listening on {localIpDisplay.Split(' ')[0]}:{ArtNetPort} for {numUniverses} universes (from {startUniverse})... No packets received.",
                          IStatusProvider.StatusLevel.Warning);
            }
            else
            {
                SetStatus($"Listening for {numUniverses} universes. Receiving {receivedCount} active universes.", IStatusProvider.StatusLevel.Success);
            }
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        StopListening();
    }

    private sealed class UniverseData
    {
        public readonly byte[] DmxData = new byte[512];
        public long LastReceivedTicks;
    }

    #region IStatusProvider & ICustomDropdownHolder
    private string _lastStatusMessage = "Inactive";
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;

    public void SetStatus(string m, IStatusProvider.StatusLevel l)
    {
        _lastStatusMessage = m;
        _lastStatusLevel = l;
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
    public string GetStatusMessage() => _lastStatusMessage;

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
        public string DisplayName => $"{Name} ({IpAddress})";
    }
    #endregion

    string ICustomDropdownHolder.GetValueForInput(Guid id) => id == LocalIpAddress.Id ? LocalIpAddress.Value : string.Empty;

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