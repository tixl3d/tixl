using System.Threading;

namespace T3.Core.DataTypes;

/// <summary>
/// Global master enable for all laser output, driven by the menu-bar
/// emergency button. When disabled, the EtherDream transport stops the DAC
/// outright (playback halted, light source off, connection kept warm for an
/// instant resume); the UDP transports keep sending the last geometry with
/// zeroed colors so scanners stay settled. Defaults to OFF: laser output only
/// flows after the user explicitly arms it. Volatile: the editor UI thread
/// writes it while output operators read it from their send threads.
/// </summary>
public static class LaserMasterEnable
{
    private static volatile bool _enabled;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
