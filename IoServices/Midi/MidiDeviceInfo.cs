#nullable enable
namespace T3.IoServices.Midi;

/// <summary>
/// Identifies a MIDI port as the user sees it. Replaces NAudio's <c>MidiInCapabilities</c> /
/// <c>MidiOutCapabilities</c>, which exist only on Windows.
/// </summary>
/// <param name="ProductName">
/// The name settings and recordings match against, so it has to stay stable across a rescan.
/// </param>
/// <param name="ProductId">
/// Tells two identically named devices apart in recordings. Only WinMM reports one; 0 means unknown.
/// </param>
public readonly record struct MidiDeviceInfo(string ProductName, int ProductId = 0);
