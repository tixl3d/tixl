#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Per-composition recording configuration. Drives which sources the timeline Record
/// button hooks into when the user clicks it. Editor-only — <see cref="T3.IoServices.IoDataSetRecorder"/>
/// takes plain booleans, so there's no Core consumer for these flags. Lives on
/// <see cref="T3.Editor.UiModel.SymbolUi.RecordingSettings"/> alongside
/// <see cref="T3.Editor.Gui.Windows.RenderExport.RenderSettings"/> for the same reason
/// the latter does: project-side UI-only state.
/// </summary>
/// <remarks>
/// Permissive defaults — a composition without an explicit
/// <see cref="T3.Editor.UiModel.SymbolUi.RecordingSettings"/> block captures everything
/// (matches the pre-config behaviour of the recording feature). Serialised into the
/// <c>.t3ui</c> sidecar by <c>SymbolUiJson</c>.
/// </remarks>
internal sealed class RecordingSettings
{
    public static readonly RecordingSettings Defaults = new();

    /// <summary>Capture microphone / loopback audio via WASAPI on Record.</summary>
    public bool CaptureAudio = true;

    /// <summary>Capture IO events (MIDI + OSC) into a <c>.data</c> file on Record.</summary>
    public bool CaptureIo = true;

    /// <summary>
    /// When <see cref="CaptureIo"/> is on, also subscribe to MIDI events. Lets the user
    /// record just OSC by turning this off — useful when a MIDI controller is plugged in
    /// for live input but its events shouldn't end up in the timeline take.
    /// </summary>
    public bool CaptureMidi = true;

    /// <summary>
    /// When <see cref="CaptureIo"/> is on, also subscribe to OSC events on the
    /// configured port. Symmetric to <see cref="CaptureMidi"/>.
    /// </summary>
    public bool CaptureOsc = true;

    #region Serialization

    /// <summary>
    /// Lightweight detector for "non-default state worth persisting." Keeps the
    /// <c>.t3ui</c> file lean for compositions that never touched these settings.
    /// </summary>
    internal bool IsAtDefault()
        => CaptureAudio == Defaults.CaptureAudio
        && CaptureIo == Defaults.CaptureIo
        && CaptureMidi == Defaults.CaptureMidi
        && CaptureOsc == Defaults.CaptureOsc;

    internal void WriteToJson(JsonTextWriter writer)
    {
        writer.WritePropertyName("Recording");
        writer.WriteRawValue(JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    internal static RecordingSettings? ReadFromJson(JToken parentToken)
    {
        var token = parentToken["Recording"];
        if (token == null)
            return null;

        return token.ToObject<RecordingSettings>();
    }

    #endregion
}
