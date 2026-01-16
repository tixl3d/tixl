using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Operators.Utils;
using Rug.Osc;

namespace Lib.io.osc;

[Guid("3a1d7ea0-5445-4df0-b08a-6596e53f815a")]
internal sealed class OscInput : Instance<OscInput>, OscConnectionManager.IOscConsumer, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "F697732E-46F3-4037-AFC5-56F396BD70AD", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Dict<float>> Contents = new();

    [Output(Guid = "1E2EC3D2-B242-4E6F-8D15-290584315AA9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<float>> Values = new();

    [Output(Guid = "3291E15A-1900-4252-8591-D016281527F0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasTrigger = new();

    public OscInput()
    {
        Values.UpdateAction += Update;
        Contents.UpdateAction += Update;

        WasTrigger.UpdateAction += AnimatedUpdate;
    }

    private bool _isListening;
    private readonly List<string> _groupingKeys = new();
    private readonly List<string> _filterKeys = new();

    private void Update(EvaluationContext context)
    {
        if (Math.Abs(_lastUpdateFrame - context.LocalFxTime) < 0.001f)
            return;

        _lastUpdateFrame = context.LocalFxTime;

        var shouldClear = false;

        var useKeyValuePairs = UseKeyValuePairs.GetValue(context);
        if (useKeyValuePairs != _useKeyValuePairs)
        {
            shouldClear = true;
            _useKeyValuePairs = useKeyValuePairs;
        }

        if (GroupKeysAsPaths.DirtyFlag.IsDirty)
        {
            _groupingKeys.Clear();
            _groupingKeys.AddRange(GroupKeysAsPaths.GetValue(context).Split(","));
            shouldClear = true;
        }

        if (FilterKeys.DirtyFlag.IsDirty)
        {
            _filterKeys.Clear();
            var filters = FilterKeys.GetValue(context);
            if (string.IsNullOrEmpty(filters))
            {
                _filterKeys.Clear();
            }
            else
            {
                _filterKeys.AddRange(filters.Split(","));
            }

            shouldClear = true;
        }

        // Update address and give connectivity status 
        _searchFilterKey = SearchFilterKey.GetValue(context);

        var filterPattern = SearchPattern.GetValue(context);
        if (filterPattern != _filterPattern)
        {
            _filterPattern = filterPattern;
            if (!string.IsNullOrEmpty(filterPattern))
            {
                try
                {
                    _filterRegex = new Regex(filterPattern);
                }
                catch (Exception e)
                {
                    SetStatus("Invalid regex pattern: " + e.Message, IStatusProvider.StatusLevel.Warning);
                    _filterRegex = null;
                    return;
                }
            }

            shouldClear = true;
        }

        if (Address.DirtyFlag.IsDirty || Port.DirtyFlag.IsDirty)
            UpdateStatusMessage();

        var isListening = IsListening.GetValue(context);
        var isListeningChanged = isListening != _isListening;
        if (isListeningChanged)
        {
            _isListening = isListening;
        }

        var newAddress = Address.GetValue(context);
        if (newAddress != _address)
        {
            shouldClear = true;
            _address = newAddress;
        }

        var newPort = Port.GetValue(context);
        var portChanged = newPort != _port;
        if (portChanged || isListeningChanged)
        {
            shouldClear = true;

            if (newPort < 0 || newPort > 65535)
            {
                SetStatus("Invalid port number", IStatusProvider.StatusLevel.Warning);
                return;
            }

            // Unregister old port
            if (_isConnected && (_port != UndefinedPortId || !isListening))
            {
                // Log.Debug("Unregister after isListeningChanged", this);
                OscConnectionManager.UnregisterConsumer(this);
                _isConnected = false;
            }

            if (isListening)
            {
                // Log.Debug($"Register {newPort}", this);
                OscConnectionManager.RegisterConsumer(this, newPort);
                _isConnected = true;
            }

            _port = newPort;
            UpdateStatusMessage();
        }

        // Update output
        _printLogMessages = PrintLogMessages.GetValue(context);

        Values.Value = _collectedFloatResults;

        lock (_valuesByKeys)
        {
            if (shouldClear)
                _valuesByKeys.Clear();

            Contents.Value = _valuesByKeys;
        }

        Values.DirtyFlag.Clear();
        Contents.DirtyFlag.Clear();
    }

    /// <summary>
    /// WasHit needs to send true and false to trigger events in connected operators.
    /// To avoid updating the rest of the operator on every frame, we have have this
    /// alternative update method that only sends the trigger value. 
    /// </summary>
    private void AnimatedUpdate(EvaluationContext context)
    {
        if (Math.Abs(_lastUpdateFrame - context.LocalFxTime) < 0.001f)
            return;

        _lastUpdateFrame = context.LocalFxTime;
        Update(context);

        WasTrigger.Value = _wasTrigger;
        _wasTrigger = false;
    }

    private bool _wasTrigger;

    private void UpdateStatusMessage()
    {
        // Update status message
        var portIsActive = OscConnectionManager.TryGetScannedAddressesForPort(_port, out var addresses) && addresses.Count > 0;
        var addressIsDefined = !string.IsNullOrEmpty(_address);
        var addressIsActive = addressIsDefined && portIsActive && addresses.ContainsKey(_address);

        if (addressIsActive)
        {
            SetStatus(string.Empty, IStatusProvider.StatusLevel.Success);
            return;
        }

        if (addressIsDefined && portIsActive)
        {
            SetStatus($"No messages for {_address} on {_port}.", IStatusProvider.StatusLevel.Warning);
            return;
        }

        if (portIsActive)
        {
            SetStatus("Please use dropdown to pick an active address.", IStatusProvider.StatusLevel.Warning);
            return;
        }

        var ipAddress = GetLocalIpAddress();
        SetStatus($"""
                   Listening on port {_port} at...
                   {ipAddress}

                   No messages received, yet.
                   Tip: Make sure app is not blocked for Local Network on your mobile phone. 
                   """, IStatusProvider.StatusLevel.Notice);
    }

    # region handling async messages from other thread
    public void ProcessMessage(OscMessage msg)
    {
        lock (this)
        {
            if (_printLogMessages)
            {
                Log.Debug($"Received OSC: {msg}", this);
            }

            if (!string.IsNullOrEmpty(_address) && !msg.Address.StartsWith(_address))
                return;

            if (!ParseMessages(msg))
                return;

            SetStatus(string.Empty, IStatusProvider.StatusLevel.Success);
            FlagAsDirty();
        }
    }

    /// <summary>
    /// This will cause Update to be called on next frame 
    /// </summary>
    private void FlagAsDirty()
    {
        Contents.DirtyFlag.Invalidate();
        Values.DirtyFlag.Invalidate();
        _wasTrigger = true;
    }

    private bool ParseMessages(OscMessage m)
    {
        lock (_valuesByKeys)
        {
            if (m.Count == 0)
                return false;

            _collectedFloatResults.Clear();
            if (_useKeyValuePairs)
            {
                if (m.Count % 2 != 0)
                {
                    SetStatus("Osc message has odd number of elements, can't be used as key value pairs",
                              IStatusProvider.StatusLevel.Warning);
                    return false;
                }

                if (!string.IsNullOrEmpty(_searchFilterKey))
                {
                    var foundMatch = false;
                    for (var index = 0; index < m.Count; index += 2)
                    {
                        if (m[index] is not string key)
                            continue;

                        if (key != _searchFilterKey)
                            continue;

                        var valueAsString = m[index + 1].ToString();

                        if (valueAsString == null)
                            continue;

                        if (_filterRegex != null)
                        {
                            if (!_filterRegex.IsMatch(valueAsString))
                                continue;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(valueAsString))
                                continue;
                        }

                        foundMatch = true;
                        break;
                    }

                    if (!foundMatch)
                    {
                        if (_printLogMessages)
                        {
                            Log.Debug($"Skipping OSC message not matching {_searchFilterKey} == {_filterPattern}", this);
                        }

                        return false;
                    }
                }

                var groupingSuffix = "";
                if (_groupingKeys.Count > 0)
                {
                    foreach (var groupKey in _groupingKeys)
                    {
                        for (var index = 0; index < m.Count; index += 2)
                        {
                            if (m[index] is not string key)
                                continue;

                            if (groupKey.StartsWith("$"))
                            {
                                if (key == groupKey.Substring(1))
                                {
                                    groupingSuffix += key + "_" + m[index + 1] + "/";
                                }
                            }
                            else
                            {
                                if (key == groupKey)
                                {
                                    groupingSuffix += m[index + 1] + "/";
                                }
                            }

                            // Adding the key as prefix might help but leads to cluttered paths
                            //groupingSuffix += key + "_" + m[index + 1]  + "/";
                        }
                    }
                }

                for (var index = 0; index < m.Count; index += 2)
                {
                    if (m[index] is not string key)
                    {
                        SetStatus("Expected key but got " + m[index].GetType().Name,
                                  IStatusProvider.StatusLevel.Warning);
                        break;
                    }

                    if (_groupingKeys.Contains(key))
                        continue;

                    if (_filterKeys.Count > 0 && !_filterKeys.Contains(key))
                        continue;

                    if (OscConnectionManager.TryGetFloatFromMessagePart(m[index + 1], out var floatValue))
                    {
                        _valuesByKeys[m.Address + "/" + groupingSuffix + key] = floatValue;
                    }

                    _collectedFloatResults.Add(floatValue);
                }
            }
            else
            {
                for (var index = 0; index < m.Count; index++)
                {
                    if (OscConnectionManager.TryGetFloatFromMessagePart(m[index], out var floatValue))
                    {
                        var path = m.Count == 1
                                       ? OscConnectionManager.BuildMessageComponentPath(m)
                                       : OscConnectionManager.BuildMessageComponentPath(m, index);
                        _valuesByKeys[path] = floatValue;
                    }

                    _collectedFloatResults.Add(floatValue);
                }
            }

            return true;
        }
    }
    # endregion

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        OscConnectionManager.UnregisterConsumer(this);
    }

    private static string GetLocalIpAddress()
    {
        var ipAddresses = GetLocalIPv4Addresses();
        if (ipAddresses.Count == 0)
        {
            return "unknown IP";
        }

        return string.Join("\n", ipAddresses);

        // return Dns.GetHostEntry(Dns.GetHostName())
        //           .AddressList
        //           .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?
        //           .ToString();

        // using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        //
        // socket.Connect("8.8.8.8", 65530);
        // if (socket.LocalEndPoint is IPEndPoint endPoint)
        // {
        //     return endPoint.Address.ToString();
        // }
        //
        // return "unknown IP";
    }

    public static List<string> GetLocalIPv4Addresses()
    {
        var ipAddressList = new List<string>();

        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            Console.WriteLine("Network is not available.");
            return ipAddressList; // Return empty list if network is down
        }

        try
        {
            // Get all network interfaces
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Filter for interfaces that are currently operational (Status == Up)
                // and are not Loopback or Tunnel interfaces
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    // Get the IP properties for this interface
                    IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();

                    // Iterate through all unicast addresses assigned to this interface
                    foreach (UnicastIPAddressInformation ipInfo in ipProperties.UnicastAddresses)
                    {
                        // Filter for IPv4 addresses
                        if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            // Add the IPv4 address string to the list
                            ipAddressList.Add(ipInfo.Address.ToString());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log potential exceptions (e.g., permission issues)
            Log.Debug($"Error retrieving IP addresses: {ex.Message}");
            // Depending on requirements, you might want to throw, return null, or just the empty list.
        }

        return ipAddressList;
    }

    #region implement status provider
    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        _lastWarningMessage = message;
        _statusLevel = level;
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string GetStatusMessage() => _lastWarningMessage;

    private string _lastWarningMessage = "Not updated yet.";
    private IStatusProvider.StatusLevel _statusLevel;
    #endregion

    #region address dropdown
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return Address.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != Address.Id)
        {
            yield return "undefined";
            yield break;
        }

        if (!OscConnectionManager.TryGetScannedAddressesForPort(Port.Value, out var addresses))
        {
            Log.Warning("No addresses found for port " + Port.Value);
            yield break;
        }

        var listOfOrderedKeyValuePairs = addresses.OrderBy(x => x.Key).ToList();

        foreach (var (address, type) in listOfOrderedKeyValuePairs)
        {
            yield return $"{address}{Separator}<{type}>";
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected))
            return;

        var parts = selected.Split(Separator);
        if (parts.Length > 1)
        {
            Address.SetTypedInputValue(parts[0]);
        }
        else
        {
            Address.SetTypedInputValue(selected);
        }
    }
    #endregion

    private double _lastUpdateFrame = -1;
    private bool _isConnected;

    private bool _useKeyValuePairs;
    private string _searchFilterKey;
    private string _filterPattern;
    private Regex _filterRegex;

    private readonly List<float> _collectedFloatResults = new(10);
    private readonly Dict<float> _valuesByKeys = new(0f);

    private const int UndefinedPortId = -1;
    private int _port = UndefinedPortId;
    private string _address;

    private bool _printLogMessages;

    [Input(Guid = "87EFD3C4-F2DF-4996-924F-12C631BAD8D8")]
    public readonly InputSlot<int> Port = new();

    [Input(Guid = "17D1FE47-430A-4465-92AA-92A4EFFB515F")]
    public readonly InputSlot<string> Address = new();

    [Input(Guid = "8014A7A6-CACB-4206-A5B4-87C14235A20C")]
    public readonly InputSlot<bool> UseKeyValuePairs = new();

    [Input(Guid = "D9470564-3629-49FC-B9A2-4EA8B5AF6B60")]
    public readonly InputSlot<string> GroupKeysAsPaths = new();

    [Input(Guid = "ABC7817D-25DC-479B-984E-73B49D9ADE5F")]
    public readonly InputSlot<string> FilterKeys = new();

    [Input(Guid = "8E5D30A3-5878-4F64-9EB4-AD5782A957BF")]
    public readonly InputSlot<string> SearchFilterKey = new();

    [Input(Guid = "DBF1C777-D399-49BC-ACB0-335CD1F7FA81")]
    public readonly InputSlot<string> SearchPattern = new();

    [Input(Guid = "6C15E743-9A70-47E7-A0A4-75636817E441")]
    public readonly InputSlot<bool> PrintLogMessages = new();

    [Input(Guid = "3B179FF2-172A-4FDA-8E26-7BB3E80628D0")]
    public readonly InputSlot<bool> IsListening = new();

    private const string Separator = " - ";
}