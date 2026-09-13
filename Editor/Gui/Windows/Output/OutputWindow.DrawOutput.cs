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

using T3.Editor.Gui.Windows.OutputSetup;

namespace T3.Editor.Gui.Windows.Output;

internal sealed partial class OutputWindow
{
    /// <summary>Evaluates the shown op from its own output and draws it. Evaluation always starts at the shown op:
    /// a separate start op used to exist and left windows stuck on an op with no output.</summary>
    private Type? UpdateAndDrawOutput(Instance? instance)
    {
        if (instance == null || instance.Outputs.Count <= 0)
            return null;

        var evaluatedSymbolUi = instance.GetSymbolUi();
        var evalOutput = Pinning.GetPinnedOrDefaultOutput(instance.Outputs);

        if (evalOutput == null || !evaluatedSymbolUi.OutputUis.TryGetValue(evalOutput.Id, out var evaluatedOutputUi))
            return null;

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

        // Already rendered this frame for an output the setup presents: show that texture rather than
        // invalidating the chain and rendering the same scene a second time at this window's resolution.
        var alreadyRendered = evalOutput is Slot<Texture2D> textureSlot
                              && textureSlot.Value is { IsDisposed: false } shown
                              && OutputContentResolver.WasContentPulledThisFrame(shown);

        // Render!
        evaluatedOutputUi.DrawValue(evalOutput, EvaluationContext, Config.Title, recompute: !alreadyRendered);
        return evalOutput.ValueType;
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
