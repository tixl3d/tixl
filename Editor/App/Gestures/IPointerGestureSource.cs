#nullable enable

namespace T3.Editor.App.Gestures;

/// <summary>
/// Where trackpad gestures come from on this platform. A source translates whatever the platform sends into
/// <see cref="PointerGestures"/>; the editor itself only reads the result, so nothing above this line knows
/// about SDL, Wayland or Win32.
/// </summary>
internal interface IPointerGestureSource
{
    /// <summary>
    /// True where the platform reports pinches as gestures. Without it the editor falls back to the Ctrl+scroll
    /// convention, which is what Windows trackpad drivers send.
    /// </summary>
    bool ProvidesPinch { get; }

    /// <summary>A name for the log, so it is clear which path a machine took.</summary>
    string Description { get; }
}
