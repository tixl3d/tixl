#nullable enable
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Windows.TimeLine;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Changes a composition's BPM. With "keep seconds" it also retimes everything in the composition that is
/// timed in bars — clip placement, keyframes of children that aren't clips themselves (clip ops keep their
/// keys in clip source time), the loop range and the playhead — so all of it stays at the same seconds.
/// Original values are snapshotted once, so a drag re-applies absolute factors instead of accumulating
/// rounding, and undo restores them exactly.
/// </summary>
internal sealed class ChangeBpmCommand : ICommand
{
    public string Name => "Change BPM";
    public bool IsUndoable => true;

    internal ChangeBpmCommand(Guid symbolId, float originalBpm, bool keepSeconds)
    {
        _symbolId = symbolId;
        _originalBpm = originalBpm;
        _newBpm = originalBpm;
        _keepSeconds = keepSeconds;

        if (!keepSeconds || !SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
            return;

        var symbol = symbolUi.Symbol;
        foreach (var (childId, child) in symbol.Children)
        {
            var isClip = false;
            foreach (var (outputId, output) in child.Outputs)
            {
                if (output.OutputData is not TimeClip clip)
                    continue;

                _clips.Add(new ClipEntry(childId, outputId, clip.TimeRange));
                isClip = true;
            }

            if (isClip)
                continue;

            foreach (var inputId in child.Inputs.Keys)
            {
                if (!symbol.Animator.TryGetCurvesForChildInput(childId, inputId, out var curves))
                    continue;

                for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                {
                    var keys = curves[curveIndex].GetVDefinitions();
                    var times = new double[keys.Count];
                    for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                    {
                        times[keyIndex] = keys[keyIndex].U;
                    }

                    _curves.Add(new CurveEntry(childId, inputId, curveIndex, times));
                }
            }
        }

        var playback = Playback.Current;
        if (playback != null)
        {
            _originalLoopRange = playback.LoopRange;
            _originalTimeInBars = playback.TimeInBars;
        }
    }

    /// <summary>Applies an intermediate or final BPM while the edit is still in progress.</summary>
    internal void Apply(float newBpm)
    {
        _newBpm = newBpm;
        ApplyBpm(newBpm);
    }

    public void Do() => ApplyBpm(_newBpm);

    public void Undo() => ApplyBpm(_originalBpm);

    private void ApplyBpm(float bpm)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning("Can't change BPM - composition is no longer available.");
            return;
        }

        var symbol = symbolUi.Symbol;
        symbol.CompositionSettings.Playback.Bpm = bpm;

        var playback = Playback.Current;
        if (playback != null)
            playback.Bpm = bpm;

        if (_keepSeconds && bpm > 0 && _originalBpm > 0)
        {
            // Seconds = bars * 240 / bpm, so keeping seconds means bars scale with the BPM.
            var factor = bpm / _originalBpm;
            RetimeClips(symbol, factor);
            RetimeCurves(symbol, factor);

            if (playback != null)
            {
                playback.LoopRange = new TimeRange((float)(_originalLoopRange.Start * factor), (float)(_originalLoopRange.End * factor));
                playback.TimeInBars = _originalTimeInBars * factor;
            }

            AnimationParameterEditing.CurvesTablesNeedsRefresh = true;
        }

        symbolUi.FlagAsModified();
    }

    private void RetimeClips(Symbol symbol, double factor)
    {
        foreach (var entry in _clips)
        {
            if (!symbol.Children.TryGetValue(entry.ChildId, out var child)
                || !child.Outputs.TryGetValue(entry.OutputId, out var output)
                || output.OutputData is not TimeClip clip)
                continue;

            clip.TimeRange = new TimeRange((float)(entry.OriginalRange.Start * factor), (float)(entry.OriginalRange.End * factor));
        }
    }

    private void RetimeCurves(Symbol symbol, double factor)
    {
        foreach (var entry in _curves)
        {
            if (!symbol.Animator.TryGetCurvesForChildInput(entry.ChildId, entry.InputId, out var curves)
                || entry.CurveIndex >= curves.Length)
                continue;

            var curve = curves[entry.CurveIndex];
            var keys = new List<VDefinition>(curve.GetVDefinitions());
            if (keys.Count != entry.OriginalTimes.Length)
            {
                Log.Warning("Skipping BPM retime of a curve whose keys changed meanwhile.");
                continue;
            }

            curve.BeginBatchEdit();
            foreach (var key in keys)
            {
                curve.RemoveKeyframeAt(key.U);
            }

            for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                curve.AddOrUpdateV(entry.OriginalTimes[keyIndex] * factor, keys[keyIndex]);
            }

            curve.EndBatchEdit();
        }
    }

    private readonly record struct ClipEntry(Guid ChildId, Guid OutputId, TimeRange OriginalRange);

    private readonly record struct CurveEntry(Guid ChildId, Guid InputId, int CurveIndex, double[] OriginalTimes);

    private readonly Guid _symbolId;
    private readonly float _originalBpm;
    private readonly bool _keepSeconds;
    private readonly List<ClipEntry> _clips = [];
    private readonly List<CurveEntry> _curves = [];
    private readonly TimeRange _originalLoopRange;
    private readonly double _originalTimeInBars;
    private float _newBpm;
}
