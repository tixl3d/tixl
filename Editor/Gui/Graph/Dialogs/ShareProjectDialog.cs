#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Exporting;

namespace T3.Editor.Gui.Dialogs;

/// <summary>
/// Exports a project as a shareable, source-only package file (see <see cref="ProjectPackageExporter"/>).
/// The two size reductions are opt-in and off by default: both are derived from the live graph and
/// under-approximate what the project might actually need.
/// </summary>
internal sealed class ShareProjectDialog : ModalDialog
{
    internal void ShowNextFrame(EditableSymbolProject project)
    {
        _stripUnusedSymbols = false;
        _excludeUnreferencedAssets = false;

        if (string.IsNullOrEmpty(_targetFolder))
        {
            _targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        // Flush modified symbols so the analysis reflects the current graph state
        T3Ui.Save(false);
        _analysis = ProjectPackageExporter.TryAnalyze(project, out var analysis, out _)
                        ? analysis
                        : null;
        ShowNextFrame();
    }

    public void Draw()
    {
        DialogSize = new Vector2(550, 340);

        if (BeginDialog("Share project"))
        {
            if (_analysis == null)
            {
                ImGui.TextColored(UiColors.StatusError, "Failed to analyze the project - see log for details.");
                if (CustomComponents.DrawCtaButton("Close", Icon.None, CustomComponents.ButtonStates.Emphasized))
                {
                    ImGui.CloseCurrentPopup();
                }

                EndDialogContent();
                EndDialog();
                return;
            }

            var analysis = _analysis;

            FormInputs.SetCursorToParameterEdit();
            CustomComponents.StylizedText(analysis.Project.DisplayName, Fonts.FontBold, UiColors.Text);

            var folderWarning = Directory.Exists(_targetFolder) ? null : "Folder does not exist.";
            FormInputs.AddStringInput("Folder", ref _targetFolder, null, folderWarning,
                                      "The package file is written into this folder.");

            var unusedSymbolCount = analysis.UnreachableSymbolIds.Count;
            if (analysis.CanComputeReachability && unusedSymbolCount > 0)
            {
                FormInputs.AddCheckBox($"Tree shake unused operators ({unusedSymbolCount} symbols)", ref _stripUnusedSymbols,
                                       """
                                       Excludes operators not reachable from the project's home canvas.

                                       Operators that are only temporarily disconnected or referenced
                                       from C# code count as unused, so leave this off when unsure.
                                       """);
            }

            var unusedAssetCount = analysis.UnreferencedAssetFiles.Count;
            if (unusedAssetCount > 0)
            {
                var sizeLabel = ProjectPackageExporter.FormatBytes(analysis.UnreferencedAssetBytes);
                FormInputs.AddCheckBox($"Exclude unreferenced assets ({unusedAssetCount} files, {sizeLabel})", ref _excludeUnreferencedAssets,
                                       """
                                       Excludes asset files no operator parameter refers to.

                                       File paths that are built procedurally in code can't be detected,
                                       so leave this off when unsure.
                                       """);
            }

            var hasCrossProjectReferences = analysis.CrossProjectReferences.Count > 0;
            if (hasCrossProjectReferences)
            {
                FormInputs.SetCursorToParameterEdit();
                ImGui.TextColored(UiColors.StatusError, "This project uses operators from other projects and can't be shared yet:");
                foreach (var reference in analysis.CrossProjectReferences)
                {
                    ImGui.TextColored(UiColors.TextMuted, reference);
                }
            }

            FormInputs.AddVerticalSpace(5);

            var canExport = !hasCrossProjectReferences && Directory.Exists(_targetFolder);
            if (CustomComponents.DrawCtaButton("Export", isEnabled: canExport))
            {
                if (ProjectPackageExporter.TryExport(analysis, _targetFolder,
                                                     _stripUnusedSymbols, _excludeUnreferencedAssets,
                                                     out var reason, out var packageFilePath))
                {
                    Log.Info(reason);
                    ImGui.CloseCurrentPopup();
                    BlockingWindow.Instance.ShowMessageBox($"""
                                                            The project package was saved to:

                                                            {packageFilePath}

                                                            Others can install it by unpacking it into their projects folder.
                                                            """,
                                                           "Project shared successfully");
                    CoreUi.Instance.OpenWithDefaultApplication(_targetFolder);
                }
                else
                {
                    Log.Error(reason);
                    BlockingWindow.Instance.ShowMessageBox(reason, "Failed to share project");
                }
            }

            ImGui.SameLine();
            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
            {
                ImGui.CloseCurrentPopup();
            }

            FormInputs.SetIndentToLeft();
            FormInputs.AddHint("""
                               Saves the project as a single package file that others can import.
                               Operators from built-in packages like Lib are listed as requirements
                               but not included.
                               """);
            FormInputs.SetIndentToParameters();

            EndDialogContent();
        }

        EndDialog();
    }

    private ProjectPackageExporter.Analysis? _analysis;
    private string _targetFolder = string.Empty;
    private bool _stripUnusedSymbols;
    private bool _excludeUnreferencedAssets;
}
