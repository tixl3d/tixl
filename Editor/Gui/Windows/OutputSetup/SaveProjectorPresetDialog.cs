#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>Saves an output's lens values as a projector preset, under a manufacturer and a model name.</summary>
internal sealed class SaveProjectorPresetDialog : ModalDialog
{
    /// <summary>Opens the dialog next frame, prefilled from the output's current lens shift and zoom.</summary>
    public void Show(OutputDefinition output)
    {
        var camera = output.Camera ?? new OutputDefinition.ProjectorCamera();
        var aspect = output.CanvasSize.X / MathF.Max(output.CanvasSize.Y, 1);
        _preset = new ProjectorPresets.Preset
                      {
                          ThrowRatioMin = ProjectorPresets.ThrowRatio(camera.ZoomRangeDegrees.X, aspect),
                          ThrowRatioMax = ProjectorPresets.ThrowRatio(camera.ZoomRangeDegrees.Y, aspect),
                          LensShiftPercentX = -camera.LensAxis.X * 50,
                          LensShiftPercentY = -camera.LensAxis.Y * 50,
                          ResolutionWidth = output.ResolvedResolution.Width,
                          ResolutionHeight = output.ResolvedResolution.Height,
                      };
        _isFocusPending = true;
        ShowNextFrame();
    }

    public void Draw(Setup setup, OutputDefinition output)
    {
        if (BeginDialog("Save Projector Preset"))
        {
            // Focused once as the dialog opens: auto-focus holds the keyboard every frame it's asked, so no other field could be clicked.
            FormInputs.AddStringInput("Manufacturer", ref _preset.Manufacturer, placeHolder: "Acer", autoFocus: _isFocusPending);
            _isFocusPending = false;
            FormInputs.AddStringInput("Model", ref _preset.Model, placeHolder: "X1528Ki");
            FormInputs.AddFloat("Throw ratio (wide)", ref _preset.ThrowRatioMin, 0.1f, 5, 0.01f, clampMin: true,
                                tooltip: "Throw distance over picture width with the zoom at its widest: the smaller number on a spec sheet.");
            FormInputs.AddFloat("Throw ratio (tele)", ref _preset.ThrowRatioMax, 0.1f, 5, 0.01f, clampMin: true,
                                tooltip: "Throw distance over picture width with the zoom at its narrowest; the same as wide for a fixed lens.");
            FormInputs.AddFloat("Lens shift up (%)", ref _preset.LensShiftPercentY, -150, 150, 0.5f,
                                tooltip: "How far the picture is moved up off the lens axis, in percent of its height. A table projector is usually about +50.");
            FormInputs.AddFloat("Lens shift right (%)", ref _preset.LensShiftPercentX, -100, 100, 0.5f);
            FormInputs.AddInt("Resolution width", ref _preset.ResolutionWidth, 0, 16384, tooltip: "The highest resolution the projector accepts.");
            FormInputs.AddInt("Resolution height", ref _preset.ResolutionHeight, 0, 16384);

            var isValid = !string.IsNullOrWhiteSpace(_preset.Model);
            AlignCtaButtons("Cancel", "Save");
            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
                ImGui.CloseCurrentPopup();

            ImGui.SameLine();
            if (CustomComponents.DrawCtaButton("Save", isValid))
            {
                _preset.ThrowRatioMax = MathF.Max(_preset.ThrowRatioMax, _preset.ThrowRatioMin);
                ProjectorPresets.All.Add(_preset);
                ProjectorPresets.Save();
                var title = _preset.Title;
                SetupUndo.RunUndoable($"Name projector {title}", setup,
                                      () => (output.Camera ??= new OutputDefinition.ProjectorCamera()).ModelName = title);
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
    }

    private ProjectorPresets.Preset _preset = new();
    private bool _isFocusPending;
}
