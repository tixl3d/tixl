#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Utils;

namespace T3.Editor.Gui.UiHelpers;

internal static class FileImport
{
    /// <summary>
    /// Import an external file as <see cref="Asset"/> asset or return existing.
    /// </summary>
    /// <param name="destinationFolder">
    /// Absolute folder to copy into. Null sorts the file into the subfolder matching its asset type.
    /// </param>
    public static bool TryImportDroppedFile(string sourcePath, IResourcePackage package, string? destinationFolder, [NotNullWhen(true)] out Asset? asset)
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

        // A file below a linked folder is already part of a package's asset tree, so it's referenced
        // through its virtual mount address instead of being copied into the project again.
        if (TryGetAssetInLinkedFolder(sourcePath, out asset))
            return true;

        string destFolder;
        if (destinationFolder != null)
        {
            destFolder = destinationFolder;
        }
        else
        {
            // Without an explicit target the file is deduplicated against every location auto-sorting
            // could have put it before, so re-dropping it reuses the existing asset.
            if (TryGetExistingAutoSortedAsset(package, assetType, fileName, out asset))
                return true;

            destFolder = assetType.Subfolders.Length > 0
                             ? Path.Combine(package.AssetsFolder, assetType.Subfolders[0])
                             : package.AssetsFolder;
        }

        var destFilepath = Path.Combine(destFolder, fileName);
        if (File.Exists(destFilepath))
        {
            if (AssetRegistry.TryToGetAssetFromFilepath(destFilepath, isFolder: false, out asset))
                return true;

            Log.Warning($"Existing file not registered as asset? {destFilepath}");
            return false;
        }

        // Copy to project first...
        try
        {
            Directory.CreateDirectory(destFolder);
            File.Copy(sourcePath, destFilepath);
        }
        catch (Exception)
        {
            Log.Warning($"Failed to copy to {destFilepath}");
            return false;
        }

        Log.Debug($"Imported {fileName} to {destFolder}");
        return TryRegisterImportedFile(destFilepath, package, out asset);
    }

    /// <summary>
    /// Saves encoded image data, e.g. a screenshot from the clipboard, as a timestamped PNG into
    /// the package's <c>images/pasted</c> folder and registers it as asset.
    /// </summary>
    public static bool TryImportImageData(byte[] data, string mimeType, IResourcePackage package, [NotNullWhen(true)] out Asset? asset)
    {
        asset = null;

        var pngData = data;
        if (mimeType != "image/png")
        {
            if (!TryConvertToPng(data, out var convertedData))
            {
                Log.Warning($"Can't decode the {mimeType} image from the clipboard.");
                return false;
            }

            pngData = convertedData;
        }

        var folder = Path.Combine(package.AssetsFolder, "images", "pasted");
        var filePath = GetUnusedPastedImagePath(folder);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(filePath, pngData);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to save pasted image to {filePath}: {e.Message}");
            return false;
        }

        Log.Debug($"Saved pasted image to {filePath}");
        return TryRegisterImportedFile(filePath, package, out asset);
    }

    /// <summary>
    /// True if every path of a drop payload (<c>"a|b|c"</c>) lives below a linked folder and would
    /// therefore be referenced rather than copied.
    /// </summary>
    internal static bool AreAllPathsInLinkedFolders(string dropPayload)
    {
        var foundAny = false;

        foreach (var path in dropPayload.Split('|'))
        {
            if (path.Length == 0)
                continue;

            if (!TryGetAssetInLinkedFolder(path, out _))
                return false;

            foundAny = true;
        }

        return foundAny;
    }

    private static bool TryRegisterImportedFile(string filePath, IResourcePackage package, [NotNullWhen(true)] out Asset? asset)
    {
        asset = null;

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(filePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to get fileinfo after importing to {filePath} " + e.Message);
            return false;
        }

        asset = AssetRegistry.RegisterNewFile(fileInfo, package);

        // Force the asset library window (and any other UI that polls this counter) to refresh
        // immediately. The file system watcher would also bump it eventually, but its debounce
        // delays the UI update for ~400ms after the import.
        ResourceFileWatcher.FileStateChangeCounter++;

        return true;
    }

    private static bool TryConvertToPng(byte[] data, [NotNullWhen(true)] out byte[]? pngData)
    {
        pngData = null;
        try
        {
            var image = StbImageSharp.ImageResult.FromMemory(data, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            using var stream = new MemoryStream();
            new StbImageWriteSharp.ImageWriter().WritePng(image.Data, image.Width, image.Height,
                                                          StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
            pngData = stream.ToArray();
            return true;
        }
        catch (Exception e)
        {
            Log.Debug($"Image conversion failed: {e.Message}");
            return false;
        }
    }

    /** A timestamp keeps pasted images in paste order and readable in the asset library. */
    private static string GetUnusedPastedImagePath(string folder)
    {
        var baseName = $"pasted_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var filePath = Path.Combine(folder, baseName + ".png");
        for (var index = 2; File.Exists(filePath); index++)
            filePath = Path.Combine(folder, $"{baseName}_{index}.png");

        return filePath;
    }

    private static bool TryGetExistingAutoSortedAsset(IResourcePackage package, AssetType assetType, string fileName, [NotNullWhen(true)] out Asset? asset)
    {
        asset = null;

        foreach (var subFolder in assetType.Subfolders)
        {
            var candidate = Path.Combine(package.AssetsFolder, subFolder, fileName);
            if (File.Exists(candidate) && AssetRegistry.TryToGetAssetFromFilepath(candidate, isFolder: false, out asset))
                return true;
        }

        var inRoot = Path.Combine(package.AssetsFolder, fileName);
        return File.Exists(inRoot) && AssetRegistry.TryToGetAssetFromFilepath(inRoot, isFolder: false, out asset);
    }

    private static bool TryGetAssetInLinkedFolder(string sourcePath, [NotNullWhen(true)] out Asset? asset)
    {
        asset = null;

        string absolutePath;
        try
        {
            absolutePath = Path.GetFullPath(sourcePath).ToForwardSlashes();
        }
        catch (Exception)
        {
            return false;
        }

        return AssetLinkFolders.TryGetMountForAbsolutePath(absolutePath, out _, out var relativePart)
               && relativePart.Length > 0
               && AssetRegistry.TryToGetAssetFromFilepath(absolutePath, isFolder: false, out asset);
    }
}