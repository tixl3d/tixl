#nullable enable

using ImGuiNET;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Inspector for selected <see cref="TimelineAudioClip"/>s, drawn from the Parameter
/// window when one or more audio clips are selected (and no operator is). Provides the
/// editable fields that don't fit in the timeline clip area's direct manipulation: asset
/// path, source offset / duration, volume, layer index, and the main-soundtrack flag.
/// </summary>
internal static class TimelineAudioClipInspector
{
    /// <summary>
    /// Draws the audio-clip inspector if any clips are selected on the active timeline.
    /// Returns true when something was drawn — the caller should skip the regular
    /// parameter-window content in that case.
    /// </summary>
    public static bool TryDraw(Instance? composition,
                               IReadOnlyCollection<Guid>? selectedClipIds)
    {
        if (composition == null || selectedClipIds == null || selectedClipIds.Count == 0)
            return false;

        var allClips = composition.Symbol.CompositionSettings.Playback.AudioClips;

        // Resolve selection to clip references. Skip stale IDs (selection set can lag
        // deletion by one frame).
        _selectedScratch.Clear();
        foreach (var clip in allClips)
        {
            if (selectedClipIds.Contains(clip.Id))
                _selectedScratch.Add(clip);
        }

        if (_selectedScratch.Count == 0)
            return false;

        DrawHeader(_selectedScratch);

        // Single clip: full editor. Multiple: simplified shared-fields view that hides
        // the per-clip-identity fields (asset path, length) since editing them in bulk
        // doesn't make sense.
        if (_selectedScratch.Count == 1)
            DrawSingleClip(_selectedScratch[0]);
        else
            DrawBulkFields(_selectedScratch);

        return true;
    }

    private static void DrawHeader(List<TimelineAudioClip> clips)
    {
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(clips.Count == 1
                                  ? "Audio Clip"
                                  : $"Audio Clips ({clips.Count})");
        ImGui.PopFont();

        FormInputs.AddVerticalSpace(4);
    }

    private static void DrawSingleClip(TimelineAudioClip clip)
    {
        // Asset path — read-only. Editing the path on an existing clip points it at a
        // different file, which is a structural change better handled by deleting and
        // re-dropping. Show as a copyable text field rather than a writable input.
        FormInputs.DrawInputLabel("Asset");
        var assetPath = clip.AssetPath ?? string.Empty;
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(string.IsNullOrEmpty(assetPath) ? "(unset — recording in progress?)" : assetPath);
        ImGui.PopStyleColor();

        if (clip.LengthInSeconds > 0)
        {
            FormInputs.DrawInputLabel("Source Length");
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted($"{clip.LengthInSeconds:0.00} s");
            ImGui.PopStyleColor();
        }

        FormInputs.AddVerticalSpace(2);

        var volume = clip.Volume;
        if (FormInputs.AddFloat("Volume", ref volume, min: 0, max: 4, scale: 0.005f,
                                clampMin: true, defaultValue: 1f))
        {
            clip.Volume = volume;
        }

        var srcOffsetSecs = (float)clip.SourceOffsetSecs;
        if (FormInputs.AddFloat("Source Offset (s)", ref srcOffsetSecs, min: 0, scale: 0.01f,
                                clampMin: true, defaultValue: 0f))
        {
            clip.SourceOffsetSecs = srcOffsetSecs;
        }

        var srcDurationSecs = (float)clip.SourceDurationSecs;
        if (FormInputs.AddFloat("Source Duration (s)", ref srcDurationSecs, min: 0, scale: 0.01f,
                                clampMin: true, defaultValue: 0f,
                                tooltip: "0 means: play until the end of the source file."))
        {
            clip.SourceDurationSecs = srcDurationSecs;
        }

        var layerIndex = clip.LayerIndex;
        if (FormInputs.AddInt("Layer", ref layerIndex, min: 0, max: 999, defaultValue: 0))
        {
            clip.LayerIndex = layerIndex;
        }

        FormInputs.AddVerticalSpace(4);

        var isMain = clip.IsMainSoundtrack;
        if (FormInputs.AddCheckBox("Main soundtrack", ref isMain,
                                   tooltip: "When set, this clip drives the timeline waveform background, FFT routing for AudioReaction, and the video export pipeline. Only one clip per project should be marked."))
        {
            clip.IsMainSoundtrack = isMain;
        }
    }

    /// <summary>
    /// Bulk-edit view for multi-selection. Shows only the fields that make sense to
    /// edit across multiple clips at once (volume, layer, main-soundtrack flag) and
    /// indicates when values differ.
    /// </summary>
    private static void DrawBulkFields(List<TimelineAudioClip> clips)
    {
        // Volume: edit if uniform; if mixed, show a "mixed" indicator and a single
        // input that sets all clips to the entered value on commit.
        var firstVolume = clips[0].Volume;
        var volumeMixed = clips.Any(c => Math.Abs(c.Volume - firstVolume) > 0.0001f);
        var volume = volumeMixed ? 1f : firstVolume;
        if (FormInputs.AddFloat(volumeMixed ? "Volume (mixed)" : "Volume",
                                ref volume, min: 0, max: 4, scale: 0.005f,
                                clampMin: true, defaultValue: 1f))
        {
            foreach (var c in clips)
                c.Volume = volume;
        }

        // Layer: shift all clips by the same delta. Showing absolute value when mixed
        // would force the user into "set all to N" — not usually what they want for
        // multi-select. Instead just show the lowest layer for reference.
        var minLayer = clips.Min(c => c.LayerIndex);
        var maxLayer = clips.Max(c => c.LayerIndex);
        var layer = minLayer;
        var layerLabel = minLayer == maxLayer ? "Layer" : $"Lowest Layer (range {minLayer}…{maxLayer})";
        if (FormInputs.AddInt(layerLabel, ref layer, min: 0, max: 999))
        {
            var delta = layer - minLayer;
            if (delta != 0)
            {
                foreach (var c in clips)
                    c.LayerIndex += delta;
            }
        }

        FormInputs.AddVerticalSpace(4);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted("Asset path, source offset/duration, and main-soundtrack");
        ImGui.TextUnformatted("are per-clip — select a single clip to edit them.");
        ImGui.PopStyleColor();
    }

    private static readonly List<TimelineAudioClip> _selectedScratch = new();
}
