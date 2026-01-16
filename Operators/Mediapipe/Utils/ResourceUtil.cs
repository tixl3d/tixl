using Mediapipe.External;
using Mediapipe.PInvoke;

namespace Mediapipe.Utils;

public static class ResourceUtil
{
    public static readonly string TAG = nameof(ResourceUtil);

    private static bool _IsInitialized;
    private static readonly Dictionary<string, string> _AssetPathMap = [];

    public static void EnableCustomResolver()
    {
        if (_IsInitialized) return;
        SafeNativeMethods.mp__SetCustomGlobalPathResolver__P(PathToResourceAsFile);
        SafeNativeMethods.mp__SetCustomGlobalResourceProvider__P(GetResourceContents);
        _IsInitialized = true;
    }

    /// <summary>
    ///     Registers the asset path to the resource manager.
    /// </summary>
    /// <param name="assetKey">
    ///     The key to register the asset path.
    ///     It is usually the file path of the asset hard-coded in the native code (e.g. `path/to/model.tflite`)
    ///     or the asset name (e.g. `model.bytes`).
    /// </param>
    /// <param name="assetPath">
    ///     The file path of the asset.
    /// </param>
    public static void SetAssetPath(string assetKey, string assetPath)
    {
        _AssetPathMap[assetKey] = assetPath;
    }

    /// <summary>
    ///     Registers the asset path to the resource manager.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown when the asset key is already registered
    /// </exception>
    /// <param name="assetKey">
    ///     The key to register the asset path.
    ///     It is usually the file path of the asset hard-coded in the native code (e.g. `path/to/model.tflite`)
    ///     or the asset name (e.g. `model.bytes`).
    /// </param>
    /// <param name="assetPath">
    ///     The file path of the asset.
    /// </param>
    public static void AddAssetPath(string assetKey, string assetPath)
    {
        _AssetPathMap.Add(assetKey, assetPath);
    }

    /// <summary>
    ///     Removes the asset key from the resource manager.
    /// </summary>
    /// <param name="assetKey"></param>
    public static bool RemoveAssetPath(string assetKey)
    {
        return _AssetPathMap.Remove(assetKey);
    }

    public static bool TryGetFilePath(string assetPath, out string filePath)
    {
        // try to find the file path by the requested asset path
        if (_AssetPathMap.TryGetValue(assetPath, out filePath!)) return true;
        // try to find the file path by the asset name
        if (_AssetPathMap.TryGetValue(GetAssetNameFromPath(assetPath), out filePath!)) return true;
        return false;
    }

    private static string PathToResourceAsFile(string assetPath)
    {
        try
        {
            if (TryGetFilePath(assetPath, out string filePath)) return filePath;
            throw new KeyNotFoundException($"Failed to find the file path for `{assetPath}`");
        }
        catch
        {
            return "";
        }
    }

    private static bool GetResourceContents(string path, nint dst)
    {
        try
        {
            if (!TryGetFilePath(path, out string filePath))
                throw new KeyNotFoundException($"Failed to find the file path for `{path}`");

            byte[] asset = File.ReadAllBytes(filePath!);
            using StdString srcStr = new(asset);
            srcStr.Swap(new StdString(dst, false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetAssetNameFromPath(string assetPath)
    {
        string assetName = Path.GetFileNameWithoutExtension(assetPath);
        string extension = Path.GetExtension(assetPath);

        switch (extension)
        {
            case ".binarypb":
            case ".tflite":
            {
                return $"{assetName}.bytes";
            }
            case ".pbtxt":
            {
                return $"{assetName}.txt";
            }
            default:
            {
                return $"{assetName}{extension}";
            }
        }
    }

    internal delegate string PathResolver(string path);

    internal delegate bool NativeResourceProvider(string path, nint dest);
}