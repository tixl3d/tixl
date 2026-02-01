#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Resource;
using T3.Core.Resource.Assets;

namespace T3.Editor.Gui.UiHelpers;

internal static class FileImport
{
    /// <summary>
    /// Import an external file as <see cref="Asset"/> asset or return existing.
    /// </summary>
    public static bool TryImportDroppedFile(string sourcePath, IResourcePackage package, string? subfolder, [NotNullWhen(true)] out Asset? asset)
    {
        asset = null;
        if (!Path.Exists(sourcePath))
            return false;

        var fileName = Path.GetFileName(sourcePath);

        if (!AssetType.TryGetForFilePath(sourcePath, out var assetType, out _))
        {
            Log.Warning($"Unsupported asset type {assetType}");
            return false;
        }

        var existsInSubFolder = false;
        var existsInPackageRootFolder = false;

        var destFilepath = string.Empty;

        foreach (var subFolder in assetType.Subfolders)
        {
            destFilepath = Path.Combine(package.AssetsFolder, subFolder, fileName);
            if (!File.Exists(destFilepath))
                continue;

            existsInSubFolder = true;
            break;
        }

        if (!existsInSubFolder)
        {
            destFilepath = Path.Combine(package.AssetsFolder, fileName);
            existsInPackageRootFolder = File.Exists(destFilepath);
        }

        if (existsInSubFolder || existsInPackageRootFolder)
        {
            if (AssetRegistry.TryToGetAssetFromFilepath(destFilepath, out asset))
                return true;

            Log.Warning($"Existing file not registered as asset? {destFilepath}");
            return false;
        }
        
        if (subfolder == null && assetType.Subfolders.Length > 0)
        {
            subfolder = assetType.Subfolders[0];
        }
        
        destFilepath = string.IsNullOrEmpty(subfolder)
                           ? Path.Combine(package.AssetsFolder, fileName)
                           : Path.Combine(package.AssetsFolder, subfolder, fileName);

        // Copy to project first...
        try
        {
            File.Copy(sourcePath, destFilepath);
        }
        catch (Exception)
        {
            Log.Warning($"Failed to copy to {destFilepath}");
            return false;
        }

        Log.Debug($"Imported {fileName} to {package.AssetsFolder}");

        FileInfo? destFileInfo;
        try
        {
            destFileInfo = new FileInfo(destFilepath);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to get fileinfo after dropping to {destFilepath} " + e.Message);
            return false;
        }

        asset = AssetRegistry.RegisterPackageEntry(destFileInfo, package, false);
        return true;
    }
}