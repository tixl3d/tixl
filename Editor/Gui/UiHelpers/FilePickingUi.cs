#nullable enable
using ImGuiNET;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Handles drawing a project file resource picker e.g. for StringInputUis our Soundtracks.
/// </summary>
internal static class FilePickingUi
{
    public static InputEditStateFlags DrawTypeAheadSearch(FileOperations.FilePickerTypes pickMode, 
                                                          string? fileFilter, 
                                                          ref string? filterAndSelectedPath,
                                                          bool showAssetFolderToggle = true,
                                                          bool isOutputPath = false)
    {
        ImGui.SetNextItemWidth(-70 * T3Ui.UiScaleFactor);

        var nodeSelection = ProjectView.Focused?.NodeSelection;
        if (ProjectView.Focused?.CompositionInstance == null || nodeSelection == null)
            return InputEditStateFlags.Nothing;

        var selectedInstances = nodeSelection.GetSelectedInstances().ToArray();
        SearchResourceConsumer = selectedInstances.Length == 0
                                     ? new TempResourceConsumer(ProjectView.Focused.CompositionInstance.AvailableResourcePackages)
                                     : new TempResourceConsumer(selectedInstances[0].AvailableResourcePackages);

        var pickFolder = pickMode == FileOperations.FilePickerTypes.Folder;

        // Output targets are written by the operator, so a missing file is the expected state; only an unwritable address is a problem.
        var writeFailure = string.Empty;
        var hasWarning = isOutputPath
                             ? !AssetRegistry.TryResolveAddressForWriting(filterAndSelectedPath, SearchResourceConsumer, out _, out writeFailure)
                             : !AssetRegistry.TryResolveAddress(filterAndSelectedPath, SearchResourceConsumer, out _, out _, pickFolder);
        var warningLabel = pickMode switch
                               {
                                   _ when isOutputPath && hasWarning                      => "Can't write here:\n",
                                   _ when isOutputPath                                    => string.Empty,
                                   FileOperations.FilePickerTypes.File when hasWarning   => "File doesn't exist:\n",
                                   FileOperations.FilePickerTypes.Folder when hasWarning => "Directory doesn't exist:\n",
                                   _                                                      => string.Empty
                               };

        if (warningLabel != string.Empty)
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAnimated.Rgba);

        var inputEditStateFlags = InputEditStateFlags.Nothing;
        if (filterAndSelectedPath != null)
        {
            var resultChanged = AssetInputWithTypeAheadSearch.Draw(hasWarning,
                                                                fileFilter,
                                                                ref filterAndSelectedPath,
                                                                pickFolder);

            inputEditStateFlags = resultChanged ? InputEditStateFlags.Modified : InputEditStateFlags.Nothing;
        }

        if (warningLabel != string.Empty)
            ImGui.PopStyleColor();

        var showWriteFailure = isOutputPath && hasWarning && writeFailure.Length > 0;
        if (ImGui.IsItemHovered() && filterAndSelectedPath != null && filterAndSelectedPath.Length > 0 &&
            (showWriteFailure || ImGui.CalcTextSize(filterAndSelectedPath).X > ImGui.GetItemRectSize().X))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(warningLabel + filterAndSelectedPath);
            if (showWriteFailure)
                ImGui.TextUnformatted(writeFailure);
            ImGui.EndTooltip();
        }

        if (showAssetFolderToggle)
        {
            ImGui.SameLine();

            if (ImGui.Button("...##fileSelector"))
            {
                WindowManager.ToggleInstanceVisibility<AssetLibrary>();
            }
        }

        return inputEditStateFlags;
    }

    public static TempResourceConsumer? SearchResourceConsumer;

    private readonly record struct InputResult(bool Modified, string Value);
}