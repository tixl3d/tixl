#nullable enable
namespace T3.IoServices.Midi;

/// <summary>
/// Per-op cache of the device its <c>Device</c> input refers to. Alias resolution, the connection check and
/// the status text are redone only when the requested name, the alias table or the device list changed,
/// which <see cref="MidiConnectionManager.DevicesVersion"/> tracks. Keeps the per-frame path to one int and one
/// string compare.
/// </summary>
public struct MidiDeviceNameCache
{
    /// <summary>The name as stored in the project: an alias or a physical product name.</summary>
    public string? RequestedName { get; private set; }

    /// <summary>The physical product name to match devices against.</summary>
    public string? ProductName { get; private set; }

    public bool IsConnected { get; private set; }

    /// <summary>Why the device is unavailable; null while it is connected or, for inputs, while no device is requested.</summary>
    public string? MissingMessage { get; private set; }

    /// <summary>Returns true when the resolution was redone.</summary>
    public bool Update(string? requestedName, bool forOutput)
    {
        var version = MidiConnectionManager.DevicesVersion;
        if (version == _version && requestedName == RequestedName)
            return false;

        _version = version;
        RequestedName = requestedName;
        ProductName = MidiConnectionManager.ResolveDeviceName(requestedName);

        if (string.IsNullOrEmpty(requestedName))
        {
            // An input without a device listens to all of them; an output has nowhere to send to
            IsConnected = false;
            MissingMessage = forOutput ? "No MIDI device selected." : null;
            return true;
        }

        IsConnected = forOutput
                          ? MidiConnectionManager.TryGetMidiOut(ProductName, out _)
                          : MidiConnectionManager.TryGetMidiIn(ProductName, out _);
        MissingMessage = IsConnected ? null : MidiConnectionManager.GetMissingDeviceMessage(requestedName, ProductName);
        return true;
    }

    private int _version;
}
