#nullable enable
using ImGuiNET;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed partial class AssetLibrary
{
    private static void RequestRelinkFolder(Asset mountRootAsset)
    {
        if (!AssetLinkFolders.TryGetMountById(mountRootAsset.FolderLinkMountId, out var mount))
        {
            Log.Warning("Can't find link mount for " + mountRootAsset.Address);
            return;
        }

        _relinkMountId = mount.Id;
        _relinkPath = mount.TargetRoot;
        _relinkProblem = null;
        _relinkValidatedPath = null;
        _relinkDialogRequested = true;
    }

    /// <summary>
    /// Prompt for a new target folder of a linked folder - typically after the project moved to
    /// another machine where the synced folder lives under a different path.
    /// Must be drawn at window scope every frame.
    /// </summary>
    private static void DrawRelinkFolderDialog()
    {
        if (_relinkDialogRequested)
        {
            _relinkDialogRequested = false;
            _relinkDialogOpen = true;
            ImGui.OpenPopup(RelinkDialogId);
        }

        if (!_relinkDialogOpen)
            return;

        if (!ModalDialog.BeginStaticDialog(RelinkDialogId, ref _relinkDialogOpen))
            return;

        // The mount is gone if its package was unloaded while the dialog was open
        if (!AssetLinkFolders.TryGetMountById(_relinkMountId, out var mount))
        {
            _relinkDialogOpen = false;
            ImGui.CloseCurrentPopup();
            ModalDialog.EndStaticDialog();
            return;
        }

        if (_relinkValidatedPath != _relinkPath)
        {
            _relinkValidatedPath = _relinkPath;
            _relinkProblem = string.IsNullOrWhiteSpace(_relinkPath)
                                 ? null
                                 : AssetLinkFolders.GetRelinkProblem(mount, _relinkPath);
        }

        ImGui.TextUnformatted($"Point \"{mount.MountName}\" at another folder.");

        CustomComponents.StylizedText("""
                                      In the Windows Explorer right-click the folder and choose "Copy as path"
                                      (or press Ctrl+Shift+C), then paste it below.
                                      """,
                                      Fonts.FontSmall, UiColors.TextMuted);

        FormInputs.AddVerticalSpace();

        FormInputs.AddStringInput("Folder",
                                  ref _relinkPath,
                                  "C:/Users/.../Dropbox/Footage",
                                  _relinkProblem,
                                  autoFocus: ImGui.IsWindowAppearing());

        CustomComponents.StylizedText("The previous path is remembered, so this link keeps working on the other machine.",
                                      Fonts.FontSmall, UiColors.TextMuted);

        FormInputs.AddVerticalSpace();

        ImGui.BeginDisabled(_relinkProblem != null || string.IsNullOrWhiteSpace(_relinkPath));
        if (ImGui.Button("Relink"))
        {
            if (AssetLinkFolders.TryRelink(mount, _relinkPath, out var error))
            {
                _relinkDialogOpen = false;
                ImGui.CloseCurrentPopup();
            }
            else
            {
                _relinkProblem = error;
                _relinkValidatedPath = null;
            }
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _relinkDialogOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ModalDialog.EndStaticDialog();
    }

    private static Guid _relinkMountId;
    private static string _relinkPath = string.Empty;
    private static string? _relinkValidatedPath;
    private static string? _relinkProblem;
    private static bool _relinkDialogRequested;
    private static bool _relinkDialogOpen;
    private const string RelinkDialogId = "Relink folder";
}
