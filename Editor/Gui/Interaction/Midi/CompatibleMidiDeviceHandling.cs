using System.Reflection;
using Operators.Utils;
using T3.Editor.Gui.Interaction.Midi.CompatibleDevices;
using T3.Editor.Gui.UiHelpers;
using Type = System.Type;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Handles the initialization and update of <see cref="CompatibleMidiDevice"/>s.
/// </summary>
internal static class CompatibleMidiDeviceHandling
{
    static CompatibleMidiDeviceHandling()
    {
        _compatibleControllerTypes = ScanForCompatibleDevices();
    }

    private static List<Type> ScanForCompatibleDevices()
    {
        var baseType = typeof(CompatibleMidiDevice);
        return Assembly.GetExecutingAssembly()
                       .GetTypes()
                       .Where(t => baseType.IsAssignableFrom(t) && 
                                   !t.IsAbstract && 
                                   t.GetCustomAttribute<MidiDeviceProductAttribute>() != null)
                       .ToList();
    }    
    
    internal static void InitializeConnectedDevices()
    {
        if (!MidiConnectionManager.Initialized)
        {
            //Log.Warning("MidiInConnectionManager should be initialized before InitializeConnectedDevices().");
            MidiConnectionManager.Rescan();
        }

        // Dispose devices
        foreach (var device in _connectedMidiDevices)
        {
            device.Dispose();
        }

        _connectedMidiDevices.Clear();
        
        CreateConnectedCompatibleDevices();
    }

    internal static void UpdateConnectedDevices()
    {
        foreach (var compatibleMidiDevice in _connectedMidiDevices)
        {
            compatibleMidiDevice.Update();
        }
    }

    /// <summary>
    /// Creates instances for connected known controller types.
    /// </summary>
    private static void CreateConnectedCompatibleDevices()
    {
        // Log all detected MIDI input devices for debugging
        LogMidiDebug("Scanning for compatible MIDI devices...");
        foreach (var (midiIn, midiInCapabilities) in MidiConnectionManager.MidiIns)
        {
            LogMidiDebug($"  Found MIDI input device: '{midiInCapabilities.ProductName}'");
        }
        
        foreach (var controllerType in _compatibleControllerTypes)
        {
            var attr = controllerType.GetCustomAttribute<MidiDeviceProductAttribute>(false);
            if (attr == null)
            {
                Log.Error($"{controllerType} should implement MidiDeviceProductAttribute");
                continue;
            }

            var productNames = attr.ProductNames;
            LogMidiDebug($"  Looking for controller type {controllerType.Name} with product names: {string.Join(", ", productNames.Select(n => $"'{n}'"))}");

            foreach (var (midiIn, midiInCapabilities) in MidiConnectionManager.MidiIns)
            {
                var productName = midiInCapabilities.ProductName;
                if (!productNames.Contains(productName))
                    continue;
                
                LogMidiDebug($"  Matched device '{productName}' to {controllerType.Name}");
                
                if (!MidiConnectionManager.TryGetMidiOut(productName, out var midiOut))
                {
                    Log.Error($"Can't find midi out connection for {attr.ProductNames}");
                    continue;
                }

                if (Activator.CreateInstance(controllerType) is not CompatibleMidiDevice compatibleDevice)
                {
                    Log.Error("Can't create midi-device?");
                    continue;
                }

                compatibleDevice.Initialize(midiIn, midiOut);
                _connectedMidiDevices.Add(compatibleDevice);
                Log.Debug($"Connected compatible midi device {compatibleDevice}");
            }
        }
    }

    /// <summary>
    /// Logs a debug message if MIDI debug logging is enabled in settings.
    /// </summary>
    private static void LogMidiDebug(string message)
    {
        if (UserSettings.Config.EnableMidiDebugLogging)
            Log.Debug(message);
    }

    private static readonly List<Type> _compatibleControllerTypes;
    private static readonly List<CompatibleMidiDevice> _connectedMidiDevices = new();
}