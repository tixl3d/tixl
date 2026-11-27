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
                if (folder.Asset is { IsLinkMountRoot: true })
                {
                    RenameFolderLink(folder.Asset, _state.RenameBuffer);
                }
                else
                {
                    var oldPath = Path.GetFullPath(folder.AbsolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var newPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? string.Empty, _state.RenameBuffer);
                    try
                    {
                        if (Directory.Exists(newPath) || File.Exists(newPath))
                        {
                            Log.Warning($"Rename failed: {_state.RenameBuffer} already exists");
                        }
                        else if (Directory.Exists(oldPath))
                        {
                            Directory.Move(oldPath, newPath);
                            AssetRegistry.UpdateMovedAsset(oldPath, newPath);
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
            }

            _state.RenamingInProcessId = Guid.Empty;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            _state.RenamingInProcessId = Guid.Empty;

        ImGui.SetCursorScreenPos(keepNextPos);
    }
    
    /// <summary> Renaming a linked folder renames its .tixlLink marker, never the external folder. </summary>
    private static void RenameFolderLink(Asset mountRootAsset, string newName)
    {
        if (!AssetLinkFolders.TryGetMountById(mountRootAsset.FolderLinkMountId, out var mount))
        {
            Log.Warning("Can't find link mount for " + mountRootAsset.Address);
            return;
        }

        var linkDir = Path.GetDirectoryName(mount.LinkFilePath) ?? string.Empty;
        var newLinkPath = Path.Combine(linkDir, newName + AssetLinkFolders.Extension);
        if (File.Exists(newLinkPath))
        {
            Log.Warning($"Rename failed: {newLinkPath} already exists");
            return;
        }

        try
        {
            File.Move(mount.LinkFilePath, newLinkPath);
            var package = mount.Package;
            AssetLinkFolders.UnmountLinkFile(mount.LinkFilePath);
            AssetLinkFolders.TryMount(newLinkPath, package);
        }
        catch (IOException ex)
        {
            Log.Warning($"Rename failed: {ex.Message}");
        }
    }

    /// <summary> Deletes only the .tixlLink marker - the external folder stays untouched. </summary>
    private static void RemoveFolderLink(Asset mountRootAsset)
    {
        if (!AssetLinkFolders.TryGetMountById(mountRootAsset.FolderLinkMountId, out var mount))
        {
            Log.Warning("Can't find link mount for " + mountRootAsset.Address);
            return;
        }

        try
        {
            File.Delete(mount.LinkFilePath);
        }
        catch (IOException ex)
        {
            Log.Warning($"Can't delete link file {mount.LinkFilePath}: {ex.Message}");
        }

        AssetLinkFolders.UnmountLinkFile(mount.LinkFilePath);
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
        var dropFilesResult = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.ExternalFile, out var data);

        if (dropFilesResult == DragAndDropHandling.DragInteractionResult.Hovering && data != null)
        {
            CustomComponents.BeginTooltip();
            ImGui.TextUnformatted(FileImport.AreAllPathsInLinkedFolders(data)
                                      ? "Reference files from linked folder..."
                                      : "Import files to here...");
            CustomComponents.EndTooltip();
            return;
        }

        if (dropFilesResult != DragAndDropHandling.DragInteractionResult.Dropped || data == null)
            return;

        if (!AssetRegistry.TryResolveAddress(folder.Address, null, out _, out var package, isFolder: true))
        {
            Log.Warning($"Can't resolve address ({folder.Address}) for target folder {folder}?");
            return;
        }

        // Files land in the hovered folder itself, which for a linked folder is its external target
        // rather than a directory below Assets/. A folder that vanished from disk (unresolved link)
        // must not be recreated by the import.
        if (!Directory.Exists(folder.AbsolutePath))
        {
            Log.Warning($"Can't import into missing folder {folder}");
            return;
        }

        var filePaths = data.Split("|");
        foreach (var path in filePaths)
        {
            FileImport.TryImportDroppedFile(path, package, folder.AbsolutePath, out _);
        }
    }

    /** Package roots, linked folders and folders of read-only packages stay where they are. */
    private static bool CanBeMoved(AssetFolder folder)
    {
        return folder.Asset is { IsDirectory: true, IsLinkMountRoot: false } asset
               && asset.FolderLinkMountId == Guid.Empty
               && !asset.Package.IsReadOnly
               && !folder.Address.EndsWith(AssetRegistry.PackageSeparator);
    }

    private static void HandleDropFolderIntoFolder(AssetFolder folder)
    {
        var dropResult = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.AssetFolder, out var data, () =>
                                                                     {
                                                                         CustomComponents.BeginTooltip();
                                                                         ImGui.TextUnformatted("Move folder here...");
                                                                         CustomComponents.EndTooltip();
                                                                     });

        if (dropResult == DragAndDropHandling.DragInteractionResult.Dropped && !string.IsNullOrEmpty(data))
            MoveFolderIntoFolder(data, folder);
    }

    /// <summary>
    /// Moves a folder with everything in it below the target folder, possibly into another project; the registry
    /// then rewrites all loaded references to the moved assets.
    /// </summary>
    private static void MoveFolderIntoFolder(string sourceAddress, AssetFolder target)
    {
        if (!AssetRegistry.TryGetAsset(sourceAddress, out var source) || !source.IsDirectory)
        {
            Log.Warning($"Can't resolve folder {sourceAddress}");
            return;
        }

        if (!Directory.Exists(target.AbsolutePath) || target.Asset is { Package.IsReadOnly: true })
        {
            Log.Warning($"Can't move a folder into {target.Address}");
            return;
        }

        var sourcePath = Path.GetFullPath(source.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetPath = Path.GetFullPath(target.AbsolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(Path.GetDirectoryName(sourcePath), targetPath, comparison))
            return;

        if (string.Equals(sourcePath, targetPath, comparison)
            || targetPath.StartsWith(sourcePath + Path.DirectorySeparatorChar, comparison))
        {
            Log.Warning("Can't move a folder into itself");
            return;
        }

        var newPath = Path.Combine(targetPath, Path.GetFileName(sourcePath));
        if (Directory.Exists(newPath) || File.Exists(newPath))
        {
            Log.Warning($"Can't move {Path.GetFileName(sourcePath)}: {target.Address} already contains it");
            return;
        }

        try
        {
            Directory.Move(sourcePath, newPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Can't move folder: {e.Message}");
            return;
        }

        AssetRegistry.UpdateMovedAsset(sourcePath, newPath);
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
        // An empty or unresolved folder path would move the files relative to the working directory
        if (!Directory.Exists(folder.AbsolutePath))
        {
            Log.Warning($"Can't move assets into missing folder {folder}");
            return;
        }

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

