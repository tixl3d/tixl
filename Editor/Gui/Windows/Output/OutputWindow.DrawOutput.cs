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

        // Already evaluated this frame — by the output setup compositing a send downstream of this op, or by
        // another window — and at a size this window's resolution preset accepts: show the value as it is.
        // Otherwise the preset wins and the chain renders again at this window's resolution, which for a
        // render target means it is resized back and forth every frame — hence the warning by the caption.
        var alreadyEvaluated = !evalOutput.DirtyFlag.IsDirty && evalOutput.DirtyFlag.WasUpdatedThisFrame;
        var reuse = alreadyEvaluated && PresetAcceptsValue(evalOutput);
        _imageCanvas.IsRenderedTwice = alreadyEvaluated && !reuse;

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

    public Instance? ShownInstance
    {
        get
        {
            Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
            return instance;
        }
    }
}
