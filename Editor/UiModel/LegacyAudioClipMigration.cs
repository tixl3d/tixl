#nullable enable
using ManagedBass;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.UiModel;

/// <summary>
/// Migrates a composition's legacy settings-list audio clips (<c>CompositionSettings.Playback.AudioClips</c>)
/// into ordinary visible <c>[AudioClip]</c> ops. Runs once in the startup-migration phase (after all symbol
/// packages are registered, like the variations migration): the mutation is in-memory only and the
/// migrated symbols are flagged as modified, so the change persists through the regular save machinery
/// whenever the user next saves. Each migrated clip becomes a symbol child with Path / Volume / Mute /
/// TimeRange copied and AutoPlay on; the former main soundtrack gets <c>Display = BackgroundImage</c> (which
/// carries the main-soundtrack designation) and <c>Style = Waveform</c>. Migrated entries are removed from
/// the settings list; the ops are the single source of truth afterwards.
/// </summary>
internal static class LegacyAudioClipMigration
{
    /// <summary>Migrates the symbols of all editable projects that still carry settings-list clips.
    /// Read-only packages are left untouched — they can't persist the change, and mutating them would
    /// alter their playback behaviour per launch without a trace on disk.</summary>
    internal static void MigrateLegacyClipsToOps()
    {
        foreach (var project in EditableSymbolProject.AllProjects)
        {
            foreach (var symbolUi in project.SymbolUis.Values)
            {
                TryMigrateSymbol(symbolUi);
            }
        }
    }

    private static void TryMigrateSymbol(SymbolUi symbolUi)
    {
        var symbol = symbolUi.Symbol;
        var settings = symbol.CompositionSettings;
        var clips = settings?.Playback.AudioClips;
        if (settings == null || clips == null || clips.Count == 0)
            return;

        if (!SymbolUiRegistry.TryGetSymbolUi(AudioClipSymbolId, out _))
        {
            Log.Warning($"{symbol.Name}: Can't migrate legacy audio clips — the [AudioClip] symbol isn't loaded.");
            return;
        }

        var migratedAny = false;
        for (var i = clips.Count - 1; i >= 0; i--)
        {
            if (!TryMigrateClip(symbolUi, settings, clips[i]))
                continue;

            clips.RemoveAt(i);
            migratedAny = true;
        }

        if (!migratedAny)
            return;

        // A project with a soundtrack has real playback settings (BPM etc.); without Enabled the whole
        // settings block would be dropped from the file once the clip list is empty — losing the BPM and
        // hiding the op-flagged main soundtrack from the settings walk-up.
        settings.Enabled = true;

        symbolUi.FlagAsModified();
        Log.Info($"{symbol.Name}: Migrated legacy soundtrack entry(s) to visible [AudioClip] op(s).");
    }

    private static bool TryMigrateClip(SymbolUi symbolUi, CompositionSettings settings, TimelineAudioClip clip)
    {
        if (string.IsNullOrEmpty(clip.AssetPath))
            return true; // nothing worth keeping — just drop the entry

        var symbol = symbolUi.Symbol;
        var barsPerSecond = settings.Playback.Bpm / 240.0;

        // The op registrar only plays a clip while the playhead is inside its TimeRange, so the legacy
        // "no explicit end" sentinel (End <= Start) must be resolved to the actual content length here.
        var timeRange = clip.TimeRange;
        if (timeRange.End <= timeRange.Start)
        {
            if (!TryGetClipDurationSecs(symbol, clip, out var durationSecs))
            {
                // Leave the entry in the settings list — legacy playback keeps working; retried next save.
                Log.Warning($"{symbol.Name}: Can't determine duration of '{clip.AssetPath}' — soundtrack migration deferred.");
                return false;
            }

            var audibleSecs = clip.SourceDurationSecs > 0
                                  ? clip.SourceDurationSecs
                                  : durationSecs - clip.SourceOffsetSecs;
            timeRange.End = timeRange.Start + (float)(audibleSecs * barsPerSecond);
        }

        var addCommand = new AddSymbolChildCommand(symbol, AudioClipSymbolId)
                             {
                                 PosOnCanvas = GraphUtils.FindFreePosition(symbolUi, new Vector2(0, 200), SymbolUi.Child.DefaultOpSize),
                             };
        addCommand.Do();
        var childId = addCommand.AddedChildId;

        if (!symbol.Children.TryGetValue(childId, out var child))
        {
            Log.Warning($"{symbol.Name}: Migration failed to create the [AudioClip] op.");
            return false;
        }

        // TimeRange is timeline placement; SourceRange is file-time in bars, anchored at the source offset,
        // mapping the body 1:1 onto the used slice of the file.
        foreach (var output in child.Outputs.Values)
        {
            if (output.OutputData is not TimeClip timeClip)
                continue;

            var sourceStartBars = (float)(clip.SourceOffsetSecs * barsPerSecond);
            timeClip.TimeRange = timeRange;
            timeClip.SourceRange = new TimeRange(sourceStartBars, sourceStartBars + (timeRange.End - timeRange.Start));
            timeClip.LayerIndex = clip.LayerIndex;
            break;
        }

        SetInput(symbol, child, PathInputId, new InputValue<string>(clip.AssetPath));
        SetInput(symbol, child, AutoPlayInputId, new InputValue<bool>(true));

        if (Math.Abs(clip.Volume - 1f) > 0.001f)
            SetInput(symbol, child, VolumeInputId, new InputValue<float>(clip.Volume));

        if (clip.IsMuted)
            SetInput(symbol, child, MuteInputId, new InputValue<bool>(true));

        if (clip.IsMainSoundtrack)
        {
            SetInput(symbol, child, DisplayInputId, new InputValue<int>((int)AudioClipDisplay.BackgroundImage));
            SetInput(symbol, child, StyleInputId, new InputValue<int>((int)AudioClipStyle.Waveform));
        }

        return true;
    }

    private static void SetInput(Symbol symbol, Symbol.Child child, Guid inputId, InputValue value)
    {
        // Runs at startup, before any Playback exists — ChangeInputValueCommand reads Playback.Current in
        // its constructor, so assign directly and invalidate already-created instances ourselves.
        var input = child.Inputs[inputId];
        input.IsDefault = false;
        input.Value.Assign(value);

        foreach (var parentInstance in symbol.InstancesOfSelf)
        {
            if (!parentInstance.Children.TryGetChildInstance(child.Id, out var childInstance))
                continue;

            foreach (var slot in childInstance.Inputs)
            {
                if (slot.Id != inputId)
                    continue;

                slot.DirtyFlag.ForceInvalidate();
                break;
            }
        }
    }

    private static bool TryGetClipDurationSecs(Symbol symbol, TimelineAudioClip clip, out double durationSecs)
    {
        durationSecs = clip.LengthInSeconds;
        if (durationSecs > 0)
            return true;

        // Asset resolution needs an instance for package context; at startup none may exist yet, so
        // create the parentless one on demand (only for symbols that actually carry legacy clips).
        Instance? instance = null;
        foreach (var instanceOfSelf in symbol.InstancesOfSelf)
        {
            instance = instanceOfSelf;
            break;
        }

        if (instance == null && !symbol.TryGetParentlessInstance(out instance))
            return false;

        if (!AssetRegistry.TryResolveAddress(clip.AssetPath, instance, out var absolutePath, out _))
            return false;

        var stream = AudioMixerManager.CreateOfflineAnalysisStream(absolutePath);
        if (stream == 0)
            return false;

        try
        {
            var lengthBytes = Bass.ChannelGetLength(stream);
            if (lengthBytes <= 0)
                return false;

            durationSecs = Bass.ChannelBytes2Seconds(stream, lengthBytes);
            return durationSecs > 0;
        }
        finally
        {
            AudioMixerManager.FreeOfflineAnalysisStream(stream);
        }
    }

    // [AudioClip] symbol + input ids (Operators/Lib/io/audio/AudioClip). Shared with the settings UI,
    // which edits op-provided soundtracks through these inputs.
    internal static readonly Guid AudioClipSymbolId = new("f0008b50-091d-4e9f-91eb-baa212acfa20");
    internal static readonly Guid PathInputId = new("625951af-5f99-4171-b5b0-c97413121f56");
    private static readonly Guid VolumeInputId = new("06b8b927-ec47-4392-bb67-b9a140cc852b");
    private static readonly Guid MuteInputId = new("4ad8fba6-6e13-4698-b3c6-bd5c808724ab");
    internal static readonly Guid AutoPlayInputId = new("260b61ae-7605-4f06-a3fb-793ae5a23646");
    internal static readonly Guid DisplayInputId = new("8f2e6b10-4c5d-4e8f-9a1b-2c3d4e5f6a70");
    internal static readonly Guid StyleInputId = new("9a3f7c20-5d6e-4f9a-8b2c-3d4e5f6a7b80");
}
