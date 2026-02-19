using System.IO;
using ImGuiNET;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed partial class AssetLibrary
{
    private static void HandleRenameFolder(AssetFolder folder, Vector2 lastPos)
    {
        if (_state.RenamingInProcessId != folder.Asset?.Id)
            return;

        var keepNextPos = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(lastPos);
        ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##renameFolder", ref _state.RenameBuffer, 256);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var isValidName = !string.IsNullOrEmpty(_state.RenameBuffer) && _state.RenameBuffer.IndexOfAny(['/', '\\', ':']) == -1;

            if (isValidName)
            {
                var oldPath = folder.AbsolutePath;
                var newPath = folder.AbsolutePath.Replace(folder.Name, _state.RenameBuffer);
                try
                {
                    if (Directory.Exists(oldPath))
                    {
                        Directory.Move(oldPath, newPath);
                        AssetRegistry.UpdateMovedAsset(oldPath, newPath);
                        // TODO: update all references?
                    }
                    else
                    {
                        Log.Warning($"Rename failed: Path doesn't exist: {oldPath}");
                    }
                }
                catch (IOException ex)
                {
                    Log.Warning($"Rename failed: {ex.Message}");
                }
            }

            _state.RenamingInProcessId = Guid.Empty;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            _state.RenamingInProcessId = Guid.Empty;

        ImGui.SetCursorScreenPos(keepNextPos);
    }
    
    private static void CreateSubFolder(AssetFolder folder)
    {
        var parentPath = folder.AbsolutePath;
        var newName = "New folder";
        var newPath = Path.Combine(parentPath, newName);

        int suffixCounter = 1;
        while (Directory.Exists(newPath))
        {
            newPath = Path.Combine(parentPath, $"{newName} {suffixCounter}");
            suffixCounter++;
        }

        try
        {
            // This will then create a file hock that will add the assets
            Directory.CreateDirectory(newPath);
        }
        catch (Exception e)
        {
            Log.Warning($"Can't create folder {newPath} " + e.Message);
        }
    }
    
        private static void HandleDropFilesIntoFolder(AssetFolder folder)
    {
        var dropFilesResult = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.ExternalFile, out var data, () =>
                                                                          {
                                                                              CustomComponents.BeginTooltip();
                                                                              ImGui.TextUnformatted("Import files to here...");
                                                                              CustomComponents.EndTooltip();
                                                                          });

        if (dropFilesResult != DragAndDropHandling.DragInteractionResult.Dropped || data == null)
            return;

        if (!AssetRegistry.TryResolveAddress(folder.Address, null, out _, out var package, isFolder: true))
        {
            Log.Warning($"Can't resolve address ({folder.Address}) for target folder {folder}?");
            return;
        }

        var filePaths = data.Split("|");
        foreach (var path in filePaths)
        {
            FileImport.TryImportDroppedFile(path, package, folder.Name, out _);
        }
    }

    private static void HandleDropAssetsIntoFolder(AssetFolder folder)
    {
        var dropFilesResult = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.FileAsset, out var data, () =>
                                                                          {
                                                                              CustomComponents.BeginTooltip();
                                                                              ImGui.TextUnformatted("Move assets here...");
                                                                              CustomComponents.EndTooltip();
                                                                          });

        if (dropFilesResult == DragAndDropHandling.DragInteractionResult.Dropped && !string.IsNullOrEmpty(data))
        {
            MoveAssetsToFolder(folder, data);
        }
    }

    private static void MoveAssetsToFolder(AssetFolder folder, string data)
    {
        var assetAddresses = data.Split("|");
        foreach (var address in assetAddresses)
        {
            if (!AssetRegistry.TryGetAsset(address, out var asset))
            {
                Log.Warning("Can't resolve asset? " + address);
                continue;
            }

            if (asset.FileSystemInfo == null)
            {
                Log.Warning("Skipping asset without file system info? " + asset);
                continue;
            }

            if (!asset.TryGetFileName(out var filename))
            {
                Log.Warning($"Can't get filename for {asset}");
                continue;
            }

            var targetFilePath = Path.Combine(folder.AbsolutePath, filename.ToString());
            if (File.Exists(targetFilePath))
            {
                Log.Debug("File already exists: " + targetFilePath);
                continue;
            }

            try
            {
                File.Move(asset.FileSystemInfo.FullName, targetFilePath);
            }
            catch (Exception e)
            {
                Log.Warning("Can't move file " + e.Message);
                continue;
            }

            AssetRegistry.UpdateMovedAsset(asset.FileSystemInfo.FullName, targetFilePath);
        }
    }
}

