#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.IoServices;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Inline DataClip edit pane shown below the dope sheet when
/// <see cref="TimeLineCanvas.InlineDataClipEditEnabled"/> is on AND a TimeClip with a
/// <c>DataClip</c> output is selected. The actual channel / event rendering is delegated
/// to <see cref="DataSetViewCanvas"/> in its embedded mode (X driven by the parent
/// <see cref="TimeLineCanvas"/>, no own toolbar / raster / auto-scroll), so this class
/// stays a thin host: figure out which clip the user picked, resolve its DataSet, hand
/// both to the shared canvas, and own the chrome around it (close button, empty state).
/// </summary>
internal sealed class InlineDataClipArea
{
    public InlineDataClipArea(TimeLineCanvas timeLineCanvas)
    {
        _timeLineCanvas = timeLineCanvas;
    }

    /// <summary>True when at least one selected op-TimeClip exposes a <c>Slot&lt;DataClip&gt;</c>.</summary>
    public static bool HasSelectedDataClipInstance(TimeLineCanvas timeLineCanvas, Instance compositionOp)
    {
        foreach (var timeClip in timeLineCanvas.ClipArea.OpClips.EnumerateSelectedClips())
        {
            if (!compositionOp.Children.TryGetChildInstance(timeClip.Id, out var instance) || instance == null)
                continue;

            foreach (var slot in instance.Outputs)
            {
                if (slot is Slot<DataClip?>)
                    return true;
            }
        }
        return false;
    }

    public void Draw(Instance compositionOp, float height)
    {
        if (!TryResolveSelectedDataClip(compositionOp, out var clipInstance, out var clipTimeClip))
            return;

        // Reset Y-scroll when the pane first appears OR the user picks a different clip
        // — carrying a previous pan offset over is disorienting in both cases.
        var thisFrame = ImGui.GetFrameCount();
        var justAppeared = _lastDrawnFrame != thisFrame - 1;
        _lastDrawnFrame = thisFrame;

        var selectedClipKey = clipInstance.SymbolChildId.GetHashCode();
        var clipChanged = selectedClipKey != _lastClipKey;
        _lastClipKey = selectedClipKey;

        var dataClip = TryBuildEmbeddedClip(clipInstance, clipTimeClip);
        if (dataClip == null)
        {
            DrawEmptyState(height, "Selected clip has no DataSet to display.");
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.2f).Rgba);
        ImGui.BeginChild("##inlineDataClipArea", new Vector2(0, height),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollWithMouse);
        {
            // Scroll-reset and close button live INSIDE the embedded view's Scrollable
            // child — anything we draw out here gets blocked by that child's hit-rect.
            _embeddedView.DrawEmbedded(dataClip, _timeLineCanvas,
                                       drawOverlay: () =>
                                                    {
                                                        if (clipChanged || justAppeared)
                                                            ImGui.SetScrollY(0);
                                                        TimelineDetailsArea.DrawCloseButton(_timeLineCanvas);
                                                    });
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>First selected TimeClip with a DataClip output; multi-clip editing is future work.</summary>
    private bool TryResolveSelectedDataClip(Instance compositionOp,
                                            [NotNullWhen(true)] out Instance? clipInstance,
                                            [NotNullWhen(true)] out TimeClip? clipTimeClip)
    {
        clipInstance = null;
        clipTimeClip = null;

        foreach (var timeClip in _timeLineCanvas.ClipArea.OpClips.EnumerateSelectedClips())
        {
            if (!compositionOp.Children.TryGetChildInstance(timeClip.Id, out var instance) || instance == null)
                continue;

            foreach (var slot in instance.Outputs)
            {
                if (slot is not Slot<DataClip?>)
                    continue;
                clipInstance = instance;
                clipTimeClip = timeClip;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a transient <see cref="DataClip"/> from the live DataSet + a fresh
    /// <see cref="TimeRangeMapping"/>. DataSet resolution mirrors
    /// <see cref="OutputUi.DataClipOutputUi"/>'s slot → recording → cache fall-back.
    /// </summary>
    private static DataClip? TryBuildEmbeddedClip(Instance clipInstance, TimeClip clipTimeClip)
    {
        var dataSet = TryResolveDataSet(clipInstance);
        if (dataSet == null)
            return null;

        var bpm = Playback.Current?.Bpm ?? 120.0;
        var mapping = new TimeRangeMapping(clipTimeClip.TimeRange, clipTimeClip.SourceRange, bpm);
        return new DataClip { Set = dataSet, Mapping = mapping };
    }

    private static DataSet? TryResolveDataSet(Instance instance)
    {
        foreach (var slot in instance.Outputs)
        {
            if (slot is Slot<DataClip?> { Value: { Set: not null } cached })
                return cached.Set;
        }

        if (RecordingSession.TryGetLiveDataSet(instance.SymbolChildId, out var liveSet) && liveSet != null)
            return liveSet;

        if (instance is not IDescriptiveFilename descriptive)
            return null;

        var path = descriptive.SourcePathSlot.TypedInputValue.Value;
        if (string.IsNullOrEmpty(path))
            return null;

        if (!AssetRegistry.TryResolveAddress(path, instance, out var absolutePath, out _))
            return null;

        return DataClipFiles.TryGetDataSetForFile(path, absolutePath, out var dataSet, out _) ? dataSet : null;
    }

    private static void DrawEmptyState(float height, string message)
    {
        ImGui.BeginChild("##inlineDataClipAreaEmpty", new Vector2(0, height),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        var size = ImGui.CalcTextSize(message);
        var pos = ImGui.GetWindowPos() + (ImGui.GetWindowSize() - size) * 0.5f;
        ImGui.GetWindowDrawList().AddText(pos, UiColors.TextMuted, message);
        ImGui.EndChild();
    }

    private readonly TimeLineCanvas _timeLineCanvas;

    // Shared view instance — embedded mode rendering uses the parent timeline canvas
    // for X transforms, so we hold one per host. State (channel selection, time-range
    // selection, etc.) persists across draws as it would in a standalone DataSetViewCanvas.
    private readonly DataSetViewCanvas _embeddedView = new();

    // Selected-clip identity from last frame; switching clips resets the vertical scroll
    // on the embedded view's child window.
    private int _lastClipKey;
    private int _lastDrawnFrame = -1;
}
