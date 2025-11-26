using System.Reflection;
using Operators.Utils;
using T3.Editor.Gui.Interaction.Midi.CompatibleDevices;
using Type = System.Type;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Handles the initialization and update of <see cref="CompatibleMidiDevice"/>s.
/// </summary>
public static class CompatibleMidiDeviceHandling
{
    public static void InitializeConnectedDevices()
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

    public static void UpdateConnectedDevices()
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
        foreach (var controllerType in _compatibleControllerTypes)
        {
            var attr = controllerType.GetCustomAttribute<MidiDeviceProductAttribute>(false);
            if (attr == null)
            {
                Log.Error($"{controllerType} should implement MidiDeviceProductAttribute");
                continue;
            }

            var productNames = attr.ProductNames;

            foreach (var (midiIn, midiInCapabilities) in MidiConnectionManager.MidiIns)
            {
                var productName = midiInCapabilities.ProductName;
                if (!productNames.Contains(productName))
                    continue;
                
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

    // TODO: This list could be inferred by reflection checking for MidiDeviceProductAttribute 
    private static readonly List<Type> _compatibleControllerTypes
        = new()
              {
                  typeof(Apc40Mk1),
                  typeof(Apc40Mk2),
                  typeof(ApcMini),
                  typeof(ApcMiniMk2),
                  typeof(NanoControl8)
              };

    private static readonly List<CompatibleMidiDevice> _connectedMidiDevices = new();
}