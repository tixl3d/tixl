#nullable enable
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Utils;
using Zeroconf;

namespace Lib.io.dmx;

[Guid("a7b3c8d2-1e4f-4a5b-9c6d-2e8f3a1b5c7d")]
internal sealed class WledInstance : Instance<WledInstance>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private const string WledServiceType = "_http._tcp.local.";

    // --- Discovered WLED data ---
    private readonly struct DiscoveredWled
    {
        public readonly string Name;
        public readonly string IpAddress;
        public readonly int SyncUniverse;
        public readonly int StartAddress;
        public readonly bool Multicast;
        public readonly int DmxMode;
        public readonly int SyncTimeout;
        public readonly int LedCount;

        public DiscoveredWled(string name, string ipAddress, int syncUniverse, int startAddress, bool multicast, int dmxMode, int syncTimeout, int ledCount)
        {
            Name = name;
            IpAddress = ipAddress;
            SyncUniverse = syncUniverse;
            StartAddress = startAddress;
            Multicast = multicast;
            DmxMode = dmxMode;
            SyncTimeout = syncTimeout;
            LedCount = ledCount;
        }

        public string DisplayName => $"{Name} ({IpAddress})";
    }

    // --- Static discovery (shared across all WledInstance instances) ---
    private static readonly ConcurrentDictionary<string, DiscoveredWled> _discoveredWleds = new();
    private static readonly object _discoveryLock = new();
    private static CancellationTokenSource? _discoveryCts;
    private static int _discoveryRefCount;
    private static volatile bool _isDiscovering;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly Regex WledSyncRegex = new(
        @"d\.Sf\.EM\.checked=([0-1]).*d\.Sf\.EU\.value=([0-9]+).*d\.Sf\.DA\.value=([0-9]+).*d\.Sf\.DM\.value=([0-9]).*d\.Sf\.ET\.value=([0-9]+)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // --- Instance state ---
    private bool _wasDiscovering;

    public WledInstance()
    {
        InstanceName.UpdateAction = Update;
        IpAddress.UpdateAction = Update;
        Universe.UpdateAction = Update;
        StartAddress.UpdateAction = Update;
        LedCount.UpdateAction = Update;
        Multicast.UpdateAction = Update;
        DmxMode.UpdateAction = Update;
        SyncTimeout.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var discover = Discover.GetValue(context);
        var selectedName = InstanceNameInput.GetValue(context) ?? string.Empty;

        // Start/stop discovery
        if (discover && !_wasDiscovering) StartWledDiscovery();
        else if (!discover && _wasDiscovering) StopWledDiscovery();
        _wasDiscovering = discover;

        // Default outputs from inputs
        var universe = UniverseInput.GetValue(context);
        var startAddress = StartAddressInput.GetValue(context);
        var multicast = MulticastInput.GetValue(context);
        var dmxMode = SyncModeInput.GetValue(context);
        var syncTimeout = SyncTimeoutInput.GetValue(context);
        var ledCount = LedCountInput.GetValue(context);
        var ipAddress = IpAddressInput.GetValue(context) ?? string.Empty;

        // If discovery is active, check if the selected instance name matches a discovered WLED
        if (discover && !string.IsNullOrEmpty(selectedName))
        {
            var match = _discoveredWleds.Values.FirstOrDefault(w =>
                w.DisplayName.Equals(selectedName, StringComparison.OrdinalIgnoreCase)
                );

            if (match.Name != null)
            {
                universe = match.SyncUniverse;
                startAddress = match.StartAddress;
                multicast = match.Multicast;
                dmxMode = match.DmxMode;
                syncTimeout = match.SyncTimeout;
                ledCount = match.LedCount;
                ipAddress = match.IpAddress;
            }
        }

        // Set outputs
        InstanceName.Value = selectedName;
        IpAddress.Value = ipAddress;
        Universe.Value = universe;
        StartAddress.Value = startAddress;
        StartIndex.Value = startAddress-1;
        LedCount.Value = ledCount;
        Multicast.Value = multicast;
        DmxMode.Value = dmxMode;
        SyncTimeout.Value = syncTimeout;
        
        UniverseInput.SetTypedInputValue(universe);
        StartAddressInput.SetTypedInputValue(startAddress);
        LedCountInput.SetTypedInputValue(ledCount);
        IpAddressInput.SetTypedInputValue(ipAddress);
        
        // Status
        if (!discover)
        {
            SetStatus("Discovery disabled. Set values manually or enable Discover.", IStatusProvider.StatusLevel.Notice);
        }
        else if (_discoveredWleds.IsEmpty)
        {
            SetStatus("Searching for WLED devices...", IStatusProvider.StatusLevel.Notice);
        }
        else if (!string.IsNullOrEmpty(selectedName))
        {
            var found = _discoveredWleds.Values.Any(w => w.DisplayName.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
            SetStatus(found
                ? $"Using discovered WLED '{selectedName}' ({ipAddress})"
                : $"'{selectedName}' not found in discovery. Using manual values.",
                found ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning);
        }
        else
        {
            SetStatus($"{_discoveredWleds.Count} WLED device(s) found. Select one from the dropdown.", IStatusProvider.StatusLevel.Success);
        }
    }

    #region Discovery
    private static void StartWledDiscovery()
    {
        lock (_discoveryLock)
        {
            _discoveryRefCount++;
            if (_isDiscovering) return;

            _isDiscovering = true;
            _discoveredWleds.Clear();
            _discoveryCts = new CancellationTokenSource();
            _ = Task.Run(() => DiscoverWledDevicesAsync(_discoveryCts.Token));
        }
    }

    private static void StopWledDiscovery()
    {
        lock (_discoveryLock)
        {
            if (_discoveryRefCount > 0) _discoveryRefCount--;
            if (_discoveryRefCount > 0 || !_isDiscovering) return;

            _isDiscovering = false;
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            _discoveryCts = null;
        }
    }

    private static async Task DiscoverWledDevicesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isDiscovering)
        {
            try
            {
                var responses = await ZeroconfResolver.ResolveAsync(WledServiceType, TimeSpan.FromSeconds(5), 2, 10, null, token);

                foreach (var response in responses)
                {
                    if (token.IsCancellationRequested) break;

                    var hostName = response.DisplayName;
                    var ipAddress = response.IPAddress;
                    if (string.IsNullOrEmpty(hostName) || string.IsNullOrEmpty(ipAddress)) continue;
                    if (!hostName.Contains("wled", StringComparison.OrdinalIgnoreCase)) continue;

                    var settings = await FetchWledSyncSettingsAsync(ipAddress, token);
                    var ledCount = await FetchWledLedCountAsync(ipAddress, token);
                    _discoveredWleds[ipAddress] = new DiscoveredWled(
                        hostName, ipAddress,
                        settings.SyncUniverse, settings.StartAddress,
                        settings.Multicast, settings.DmxMode, settings.SyncTimeout,
                        ledCount);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                if (_isDiscovering) Log.Error($"WLED discovery error: {e.Message}");
            }

            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task<(int SyncUniverse, int StartAddress, bool Multicast, int DmxMode, int SyncTimeout)> FetchWledSyncSettingsAsync(string ipAddress, CancellationToken token)
    {
        try
        {
            var url = $"http://{ipAddress}/settings/s.js?p=4";
            var response = await _httpClient.GetStringAsync(url, token);

            var match = WledSyncRegex.Match(response);
            if (match.Success && match.Groups.Count >= 6)
            {
                var syncType = match.Groups[1].Value;
                var universeStr = match.Groups[2].Value;
                var addressStr = match.Groups[3].Value;
                var modeStr = match.Groups[4].Value;
                var timeoutStr = match.Groups[5].Value;

                int.TryParse(universeStr, out var universe);
                int.TryParse(addressStr, out var address);
                int.TryParse(modeStr, out var mode);
                int.TryParse(timeoutStr, out var timeout);

                Log.Debug($"WLED sync settings from {ipAddress}: universe={universe}, address={address}, multicast={syncType}, mode={mode}, timeout={timeout}");
                return (universe, address, syncType == "1", mode, timeout);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to fetch WLED sync settings from {ipAddress}: {e.Message}");
        }

        return (1, 1, false, 0, 100);
    }

    private static async Task<int> FetchWledLedCountAsync(string ipAddress, CancellationToken token)
    {
        try
        {
            var url = $"http://{ipAddress}/json/";
            var response = await _httpClient.GetStringAsync(url, token);

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("info", out var info) &&
                info.TryGetProperty("leds", out var leds) &&
                leds.TryGetProperty("count", out var count))
            {
                return count.GetInt32();
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Failed to fetch WLED LED count from {ipAddress}: {e.Message}");
        }

        return 0;
    }
    #endregion

    public void Dispose()
    {
        if (_wasDiscovering) StopWledDiscovery();
    }

    #region IStatusProvider
    private string? _statusMessage;
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Notice;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _statusMessage = m; _statusLevel = l; }
    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string? GetStatusMessage() => _statusMessage;
    #endregion

    #region ICustomDropdownHolder
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == InstanceNameInput.Id) return InstanceNameInput.Value ?? string.Empty;
        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != InstanceNameInput.Id) yield break;

        if (!_isDiscovering && _discoveredWleds.IsEmpty)
        {
            yield return "Enable 'Discover' to search...";
        }
        else if (_isDiscovering && _discoveredWleds.IsEmpty)
        {
            yield return "Searching for WLED devices...";
        }
        else
        {
            foreach (var wled in _discoveredWleds.Values.OrderBy(w => w.DisplayName))
                yield return wled.DisplayName;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem) return;
        if (inputId != InstanceNameInput.Id) return;

        InstanceNameInput.SetTypedInputValue(selected);
    }
    #endregion

    #region Inputs
    [Input(Guid = "b1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e")]
    public readonly InputSlot<bool> Discover = new();

    [Input(Guid = "c2d3e4f5-a6b7-4c8d-9e0f-1a2b3c4d5e6f")]
    public readonly InputSlot<string> InstanceNameInput = new();

    [Input(Guid = "d3e4f5a6-b7c8-4d9e-0f1a-2b3c4d5e6f7a")]
    public readonly InputSlot<string> IpAddressInput = new();

    [Input(Guid = "e4f5a6b7-c8d9-4e0f-1a2b-3c4d5e6f7a8b")]
    public readonly InputSlot<int> UniverseInput = new(1);

    [Input(Guid = "f5a6b7c8-d9e0-4f1a-2b3c-4d5e6f7a8b9c")]
    public readonly InputSlot<int> StartAddressInput = new(1);

    [Input(Guid = "f6a7b8c9-d0e1-4f2a-3b4c-5d6e7f8a9b0c")]
    public readonly InputSlot<int> LedCountInput = new();

    [Input(Guid = "a6b7c8d9-e0f1-4a2b-3c4d-5e6f7a8b9c0d")]
    public readonly InputSlot<bool> MulticastInput = new();

    [Input(Guid = "b7c8d9e0-f1a2-4b3c-4d5e-6f7a8b9c0d1e")]
    public readonly InputSlot<int> SyncModeInput = new();

    [Input(Guid = "c8d9e0f1-a2b3-4c4d-5e6f-7a8b9c0d1e2f")]
    public readonly InputSlot<int> SyncTimeoutInput = new(100);
    #endregion

    #region Outputs
    [Output(Guid = "e6f7a8b9-c0d1-4e2f-3a4b-5c6d7e8f9a0b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> LedCount = new();
    
    [Output(Guid = "a2b3c4d5-e6f7-4a8b-9c0d-1e2f3a4b5c4e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> StartIndex = new();

    [Output(Guid = "d9e0f1a2-b3c4-4d5e-6f7a-8b9c0d1e2f3b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> InstanceName = new();

    [Output(Guid = "e0f1a2b3-c4d5-4e6f-7a8b-9c0d1e2f3a4c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> IpAddress = new();

    [Output(Guid = "f1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Universe = new();

    [Output(Guid = "a2b3c4d5-e6f7-4a8b-9c0d-1e2f3a4b5c6e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> StartAddress = new();

    [Output(Guid = "b3c4d5e6-f7a8-4b9c-0d1e-2f3a4b5c6d7f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Multicast = new();

    [Output(Guid = "c4d5e6f7-a8b9-4c0d-1e2f-3a4b5c6d7e8a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> DmxMode = new();

    [Output(Guid = "d5e6f7a8-b9c0-4d1e-2f3a-3a4b5c6d7e8a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> SyncTimeout = new();
    #endregion
}
