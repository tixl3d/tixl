#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Video;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

using T3.Core.Operator.Slots;
using T3.Core.Output;

using T3.Editor.Gui.Windows.OutputSetup;

namespace T3.Editor.Gui.Windows.Output;

internal sealed partial class OutputWindow
{
    /// <summary>Evaluates the shown op from its own output and draws it. Evaluation always starts at the shown op:
    /// a separate start op used to exist and left windows stuck on an op with no output.</summary>
    private Type? UpdateAndDrawOutput(Instance? instance)
    {
        _imageCanvas.SourceLabel = null;
        if (instance == null)
            return null;

        // An op without outputs of its own — typically a project's home op, which has no parent to hold them —
        // shows what its first send supplies, the same image a player of the project falls back to.
        Instance? fallbackSend = null;
        ISlot? evalOutput;
        if (instance.Outputs.Count > 0)
        {
            evalOutput = Pinning.GetPinnedOrDefaultOutput(instance.Outputs);
        }
        else if (!_sendFallback.TryGetContentSlot(instance, out fallbackSend, out evalOutput))
        {
            // Nothing to show: drop the last image so screenshots and thumbnails don't save a stale one.
            _imageCanvas.LastTexture = null;
            CustomComponents.EmptyWindowMessage(NoOutputMessage);
            return null;
        }

        if (evalOutput == null
            || !evalOutput.Parent.GetSymbolUi().OutputUis.TryGetValue(evalOutput.Id, out var evaluatedOutputUi))
            return null;

        if (fallbackSend != null)
            _imageCanvas.SourceLabel = fallbackSend.SymbolChild.ReadableName;

        if (_imageCanvas.ViewMode != ImageOutputCanvas.Modes.Fitted
            && evaluatedOutputUi is CommandOutputUi)
        {
            _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Fitted);
        }

        // Prepare context
        EvaluationContext.Reset();
        EvaluationContext.ShowGizmos = State.ShowGizmos;
        EvaluationContext.TransformGizmoMode = State.TransformGizmoMode;
        EvaluationContext.BypassCameras = _camSelectionHandling.BypassCamera;
        RequestedResolution = RenderProcess.TryGetActiveExportResolution(out var overrideResolution)
            ? overrideResolution
            : _selectedResolution.ComputeResolution();
        // A send with a Resolution of its own renders at it wherever it goes, so it does here too.
        if (fallbackSend is IContentSupplier supplier && !RenderProcess.IsExporting)
        {
            var sendResolution = supplier.GetResolution(EvaluationContext);
            if (sendResolution.Width > 0 && sendResolution.Height > 0)
                RequestedResolution = sendResolution;
        }

        EvaluationContext.RequestedResolution = RequestedResolution;

        // Set camera
        if (_camSelectionHandling.CameraForRendering != null)
        {
            EvaluationContext.SetViewFromCamera(_camSelectionHandling.CameraForRendering);
        }

        EvaluationContext.BackgroundColor = _backgroundColor;

        const string overrideSampleVariableName = "OverrideMotionBlurSamples";

        if (RenderProcess.IsExporting)
        {
            var samples = RenderProcess.GetActiveOrRequestedSettings().OverrideMotionBlurSamples;
            if (samples >= 0)
            {
                EvaluationContext.IntVariables[overrideSampleVariableName] = samples;
            }
        }
        else
        {
            EvaluationContext.IntVariables.Remove(overrideSampleVariableName);
        }

        // Already evaluated this frame — by the output setup compositing a send downstream of this op, or by
        // another window — and at a size this window's resolution preset accepts: show the value as it is.
        // Otherwise the preset wins and the chain renders again at this window's resolution, which for a
        // render target means it is resized back and forth every frame — hence the warning by the caption.
        //
        // Only textures may be reused. A Command is view-dependent: its render carries the camera this window
        // manipulates and the transform gizmos of the selected ops, and the evaluation that already happened
        // (the output setup's, which composites with gizmos off from the scene camera) also consumed this
        // frame's update. Reusing that value would freeze the preview — dragging would do nothing and no gizmo
        // would appear — so a Command is always rendered again here, at this window's own view and preset.
        var alreadyEvaluated = !evalOutput.DirtyFlag.IsDirty && evalOutput.DirtyFlag.WasUpdatedThisFrame;
        var isTexture = evalOutput is Slot<Texture2D>;
        var presetAccepts = isTexture && PresetAcceptsValue(evalOutput);
        var reuse = alreadyEvaluated && presetAccepts;
        _imageCanvas.IsRenderedTwice = alreadyEvaluated && isTexture && !presetAccepts;

        // Render!
        evaluatedOutputUi.DrawValue(evalOutput, EvaluationContext, Config.Title, recompute: !reuse);

        return evalOutput.ValueType;
    }

    /// <summary>
    /// Whether the resolution preset is content with a texture as it was rendered elsewhere: always for "Fill"
    /// (the window takes whatever size it gets), for an aspect preset when the aspect matches, for a fixed
    /// resolution when the size matches. Values that aren't textures have no size to disagree about.
    /// </summary>
    private bool PresetAcceptsValue(ISlot slot)
    {
        if (RenderProcess.IsExporting)
            return false;

        if (slot is not Slot<Texture2D> { Value: { IsDisposed: false } texture })
            return true;

        var preset = _selectedResolution;
        var width = texture.Description.Width;
        var height = texture.Description.Height;
        if (!preset.UseAsAspectRatio)
            return preset.Size.Width == width && preset.Size.Height == height;

        if (preset.Size.Width <= 0 || preset.Size.Height <= 0)
            return true;

        var presetAspect = (float)preset.Size.Width / preset.Size.Height;
        var textureAspect = (float)width / Math.Max(1, height);
        return Math.Abs(presetAspect - textureAspect) < AspectTolerance;
    }

    private const float AspectTolerance = 0.005f;

    private const string NoOutputMessage = "This operator has no output.\nAdd a [SendToOutput] to show its content here.";

    private readonly SendFallback _sendFallback = new();

    /// <summary>
    /// Finds the send of an op that has no outputs, and the texture output feeding it. The search walks
    /// symbols, so it is cached and only repeated when the op is edited, its send goes away, or — throttled —
    /// while nothing is found, since a send added deeper inside doesn't change the op's own version.
    /// </summary>
    private sealed class SendFallback
    {
        public bool TryGetContentSlot(Instance composition,
                                      [NotNullWhen(true)] out Instance? send,
                                      [NotNullWhen(true)] out ISlot? contentSlot)
        {
            contentSlot = null;
            var now = ImGui.GetTime();
            var isStale = !ReferenceEquals(_composition, composition)
                          || _version != composition.Symbol.VersionCounter
                          || _send is { IsDisposed: true }
                          || (_send == null && now - _lastSearchTime > RetryInterval);
            if (isStale)
            {
                _composition = composition;
                _version = composition.Symbol.VersionCounter;
                _lastSearchTime = now;
                ContentSupplierSearch.CollectUnder(composition, _found);
                _send = _found.Count > 0 ? _found[0] : null;
                _found.Clear();
            }

            send = _send;
            if (send == null)
                return false;

            // What the window shows is the texture output wired into the send's texture input.
            var inputs = send.Inputs;
            for (var index = 0; index < inputs.Count; index++)
            {
                if (inputs[index] is InputSlot<Texture2D> textureInput)
                    return textureInput.TryGetFirstConnection(out contentSlot) && contentSlot is Slot<Texture2D>;
            }

            return false;
        }

        private const double RetryInterval = 1.0;
        private readonly List<Instance> _found = [];
        private Instance? _composition;
        private Instance? _send;
        private int _version = -1;
        private double _lastSearchTime = double.NegativeInfinity;
    }

    public Instance? ShownInstance
    {
        get
        {
            Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
            return instance;
        }
    }
}
