#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.Core.Utils;

namespace T3.Core.Resource.Assets;

/// <summary>
/// An external folder mounted into a package's asset tree, defined by a small
/// <c>&lt;MountName&gt;.tixlLink</c> JSON marker file inside <c>Assets/</c>. The marker's basename
/// is the virtual folder name; its content points at the real location on disk. Assets below the
/// target are registered with virtual <c>Package:MountName/...</c> addresses while their
/// <see cref="Asset.FullPath"/> stays external — so nothing is copied and no filesystem links are
/// involved (junctions/symlinks interact badly with sync tools and have destructive delete semantics).
/// </summary>
public sealed class AssetLinkFolder
{
    public required Guid Id;
    public required string LinkFilePath;
    public required string MountName;

    /// <summary> Virtual path below the package's Assets/ folder, e.g. "Footage" or "sub/Footage". </summary>
    public required string VirtualDir;

    /// <summary> Resolved absolute target folder (forward slashes). May not exist — see <see cref="IsResolved"/>. </summary>
    public required string TargetRoot;

    public required bool IsResolved;
    public required IResourcePackage Package;

    internal ResourceFileWatcher? Watcher;

    public override string ToString()
    {
        return $"{Package.Name}:{VirtualDir}/ -> {TargetRoot}";
    }
}

public static class AssetLinkFolders
{
    private sealed class LinkFileContent
    {
        public Guid Id = Guid.NewGuid();

        /// <summary> Absolute candidates, most recently linked first. </summary>
        public List<string>? Targets;

        public string? TargetRelative;

        /// <summary> Back-compat: single absolute target written before <see cref="Targets"/> existed. </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Target;
    }

    public const string Extension = ".tixlLink";

    public static IReadOnlyList<AssetLinkFolder> Mounts => _mounts;

    public static bool HasLinkExtension(string path)
    {
        return path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates the marker file for linking <paramref name="targetFolder"/> as
    /// <paramref name="mountName"/> in the package's Assets/ root and returns its path.
    /// Does not mount — call <see cref="TryMount"/> with the returned path.
    /// </summary>
    public static string Write(IResourcePackage package, string targetFolder, string mountName)
    {
        var linkFilePath = Path.Combine(package.AssetsFolder, mountName + Extension).ToForwardSlashes();
        var content = new LinkFileContent
                          {
                              Id = Guid.NewGuid(),
                              Targets = [Path.GetFullPath(targetFolder).ToForwardSlashes()],
                              TargetRelative = Path.GetRelativePath(package.Folder, targetFolder).ToForwardSlashes(),
                          };

        File.WriteAllText(linkFilePath, JsonConvert.SerializeObject(content, Formatting.Indented));
        return linkFilePath;
    }

    internal static void MountAllForPackage(IResourcePackage package)
    {
        var root = package.AssetsFolder;
        if (!Directory.Exists(root))
            return;

        foreach (var linkFile in Directory.EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories))
        {
            TryMount(linkFile, package);
        }
    }

    public static bool TryMount(string linkFilePath, IResourcePackage package)
    {
        linkFilePath = linkFilePath.ToForwardSlashes();

        // A remount replaces the previous state of the same marker file
        if (TryGetMountForLinkFile(linkFilePath, out var previousMount))
        {
            Unmount(previousMount);
        }

        LinkFileContent? content;
        try
        {
            content = JsonConvert.DeserializeObject<LinkFileContent>(File.ReadAllText(linkFilePath));
        }
        catch (Exception e)
        {
            Log.Warning($"Can't read link file {linkFilePath}: {e.Message}");
            return false;
        }

        if (content == null)
        {
            Log.Warning($"Invalid link file {linkFilePath}");
            return false;
        }

        var mountName = Path.GetFileNameWithoutExtension(linkFilePath);
        var linkDir = Path.GetDirectoryName(linkFilePath) ?? package.AssetsFolder;
        var relativeDir = Path.GetRelativePath(package.AssetsFolder, linkDir).Replace("\\", "/");
        var virtualDir = relativeDir == "." ? mountName : $"{relativeDir}/{mountName}";

        var isResolved = TryResolveTarget(content, package, out var targetRoot);
        if (string.IsNullOrEmpty(targetRoot))
        {
            Log.Warning($"Link file {linkFilePath} has no target path");
            return false;
        }

        if (isResolved && !IsValidTarget(targetRoot, package, linkFilePath))
            return false;

        var mount = new AssetLinkFolder
                        {
                            Id = content.Id,
                            LinkFilePath = linkFilePath,
                            MountName = mountName,
                            VirtualDir = virtualDir,
                            TargetRoot = targetRoot,
                            IsResolved = isResolved,
                            Package = package,
                        };

        _mounts.Add(mount);
        AssetRegistry.RegisterLinkedEntry(new DirectoryInfo(targetRoot), mount, virtualDir, isDirectory: true, isMountRoot: true);

        if (isResolved)
        {
            RegisterTargetContent(mount);
            mount.Watcher = new ResourceFileWatcher(targetRoot);
            mount.Watcher.FileCreated += (_, _) => Remount(mount);
            mount.Watcher.FileDeleted += (_, _) => Remount(mount);
            mount.Watcher.FileRenamed += (_, _) => Remount(mount);
        }
        else
        {
            Log.Warning($"Linked folder target not found for {linkFilePath} -> {targetRoot}");
        }

        ResourceFileWatcher.FileStateChangeCounter++;
        return isResolved;
    }

    public static void UnmountLinkFile(string linkFilePath)
    {
        if (TryGetMountForLinkFile(linkFilePath.ToForwardSlashes(), out var mount))
        {
            Unmount(mount);
            ResourceFileWatcher.FileStateChangeCounter++;
        }
    }

    internal static void RemoveMountsForPackage(Guid packageId)
    {
        for (var i = _mounts.Count - 1; i >= 0; i--)
        {
            if (_mounts[i].Package.Id == packageId)
                Unmount(_mounts[i]);
        }
    }

    public static bool TryGetMountById(Guid id, [NotNullWhen(true)] out AssetLinkFolder? mount)
    {
        foreach (var m in _mounts)
        {
            if (m.Id != id)
                continue;

            mount = m;
            return true;
        }

        mount = null;
        return false;
    }

    public static bool TryGetMountForLinkFile(string linkFilePath, [NotNullWhen(true)] out AssetLinkFolder? mount)
    {
        foreach (var m in _mounts)
        {
            if (!string.Equals(m.LinkFilePath, linkFilePath, StringComparison.OrdinalIgnoreCase))
                continue;

            mount = m;
            return true;
        }

        mount = null;
        return false;
    }

    /// <summary>
    /// Maps an absolute path below a mounted target back to the mount and its path relative to the target root.
    /// </summary>
    public static bool TryGetMountForAbsolutePath(string absolutePath, [NotNullWhen(true)] out AssetLinkFolder? mount, out string relativePart)
    {
        foreach (var m in _mounts)
        {
            if (!m.IsResolved)
                continue;

            if (!absolutePath.StartsWith(m.TargetRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            if (absolutePath.Length > m.TargetRoot.Length && absolutePath[m.TargetRoot.Length] != '/')
                continue;

            mount = m;
            relativePart = absolutePath.Length == m.TargetRoot.Length
                               ? string.Empty
                               : absolutePath[(m.TargetRoot.Length + 1)..];
            return true;
        }

        mount = null;
        relativePart = string.Empty;
        return false;
    }

    /// <summary>
    /// Maps a package-local address path (e.g. "Footage/clip.mp4") to the absolute path below a mounted target.
    /// Used as a fallback for paths that aren't registered (yet) — e.g. files written moments ago.
    /// </summary>
    internal static bool TryGetAbsolutePathInMount(IResourcePackage package, ReadOnlySpan<char> localPath, out string absolutePath)
    {
        foreach (var m in _mounts)
        {
            if (!m.IsResolved || m.Package.Id != package.Id)
                continue;

            if (!localPath.StartsWith(m.VirtualDir, StringComparison.OrdinalIgnoreCase))
                continue;

            if (localPath.Length == m.VirtualDir.Length)
            {
                absolutePath = m.TargetRoot;
                return true;
            }

            if (localPath[m.VirtualDir.Length] != '/')
                continue;

            absolutePath = $"{m.TargetRoot}/{localPath[(m.VirtualDir.Length + 1)..]}";
            return true;
        }

        absolutePath = string.Empty;
        return false;
    }

    private static void Unmount(AssetLinkFolder mount)
    {
        mount.Watcher?.Dispose();
        mount.Watcher = null;
        _mounts.Remove(mount);
        AssetRegistry.RemoveAssetsForLinkMount(mount.Id);
    }

    private static void Remount(AssetLinkFolder mount)
    {
        // A queued watcher event can still arrive after the mount was removed
        if (!_mounts.Contains(mount))
            return;

        // Keep the existing external watcher alive: disposing it here would mutate the watcher
        // list while ResourcePackageManager is dispatching events through it.
        AssetRegistry.RemoveAssetsForLinkMount(mount.Id);
        AssetRegistry.RegisterLinkedEntry(new DirectoryInfo(mount.TargetRoot), mount, mount.VirtualDir, isDirectory: true, isMountRoot: true);
        if (Directory.Exists(mount.TargetRoot))
        {
            RegisterTargetContent(mount);
        }

        ResourceFileWatcher.FileStateChangeCounter++;
    }

    private static void RegisterTargetContent(AssetLinkFolder mount)
    {
        try
        {
            var di = new DirectoryInfo(mount.TargetRoot);

            foreach (var dirInfo in di.EnumerateDirectories("*", SearchOption.AllDirectories))
            {
                if (FileLocations.IgnoredFiles.Contains(dirInfo.Name))
                    continue;

                AssetRegistry.RegisterLinkedEntry(dirInfo, mount, GetVirtualPath(mount, dirInfo.FullName), isDirectory: true);
            }

            foreach (var fileInfo in di.EnumerateFiles("*.*", SearchOption.AllDirectories))
            {
                if (FileLocations.IgnoredFiles.Contains(fileInfo.Name))
                    continue;

                // Links inside a linked target are ignored to prevent recursion and cycles
                if (HasLinkExtension(fileInfo.Name))
                    continue;

                AssetRegistry.RegisterLinkedEntry(fileInfo, mount, GetVirtualPath(mount, fileInfo.FullName), isDirectory: false);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to scan linked folder {mount.TargetRoot}: {e.Message}");
        }
    }

    private static string GetVirtualPath(AssetLinkFolder mount, string absolutePath)
    {
        var relative = Path.GetRelativePath(mount.TargetRoot, absolutePath).Replace("\\", "/");
        return $"{mount.VirtualDir}/{relative}";
    }

    private static bool TryResolveTarget(LinkFileContent content, IResourcePackage package, out string targetRoot)
    {
        if (!string.IsNullOrEmpty(content.TargetRelative))
        {
            var fromRelative = Path.GetFullPath(Path.Combine(package.Folder, content.TargetRelative)).ToForwardSlashes();
            if (Directory.Exists(fromRelative))
            {
                targetRoot = fromRelative;
                return true;
            }
        }

        // Every machine that relinked the folder keeps its path here, so moving the project back
        // and forth between them only needs one relink per machine.
        var firstCandidate = string.Empty;
        if (content.Targets != null)
        {
            foreach (var candidate in content.Targets)
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;

                var absolute = candidate.ToForwardSlashes();
                if (Directory.Exists(absolute))
                {
                    targetRoot = absolute;
                    return true;
                }

                if (firstCandidate.Length == 0)
                    firstCandidate = absolute;
            }
        }

        if (!string.IsNullOrEmpty(content.Target))
        {
            var legacy = content.Target.ToForwardSlashes();
            if (Directory.Exists(legacy))
            {
                targetRoot = legacy;
                return true;
            }

            if (firstCandidate.Length == 0)
                firstCandidate = legacy;
        }

        // Unresolved mounts keep the recorded path so the UI can show what's missing
        targetRoot = firstCandidate;
        return false;
    }

    /// <summary>
    /// Points an existing mount at <paramref name="newTargetFolder"/> and remounts it. The previous
    /// target is kept as a fallback candidate, so the link keeps resolving on the other machine too.
    /// </summary>
    public static bool TryRelink(AssetLinkFolder mount, string newTargetFolder, [NotNullWhen(false)] out string? error)
    {
        var package = mount.Package;
        error = GetProblemForNewTarget(newTargetFolder, package, out var target);
        if (error != null)
            return false;

        var linkFilePath = mount.LinkFilePath;
        LinkFileContent? content;
        try
        {
            content = JsonConvert.DeserializeObject<LinkFileContent>(File.ReadAllText(linkFilePath));
        }
        catch (Exception e)
        {
            error = $"Can't read the link file: {e.Message}";
            return false;
        }

        content ??= new LinkFileContent { Id = mount.Id };
        content.Targets = BuildTargetCandidates(content, target);
        content.Target = null;
        content.TargetRelative = Path.GetRelativePath(package.Folder, target).ToForwardSlashes();

        try
        {
            File.WriteAllText(linkFilePath, JsonConvert.SerializeObject(content, Formatting.Indented));
        }
        catch (Exception e)
        {
            error = $"Can't write the link file: {e.Message}";
            return false;
        }

        UnmountLinkFile(linkFilePath);
        TryMount(linkFilePath, package);
        return true;
    }

    /// <summary> Returns why <paramref name="folderPath"/> can't be linked, or null if it can. </summary>
    public static string? GetRelinkProblem(AssetLinkFolder mount, string folderPath)
    {
        return GetProblemForNewTarget(folderPath, mount.Package, out _);
    }

    private static string? GetProblemForNewTarget(string folderPath, IResourcePackage package, out string target)
    {
        if (!TryNormalizeTarget(folderPath, out target))
            return "This is not a valid folder path.";

        if (!Directory.Exists(target))
            return "This folder doesn't exist.";

        return GetTargetProblem(target, package);
    }

    private static bool TryNormalizeTarget(string folderPath, out string target)
    {
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        try
        {
            // Explorer's "Copy as path" wraps the path in quotes
            target = Path.GetFullPath(folderPath.Trim().Trim('"')).ToForwardSlashes().TrimEnd('/');
        }
        catch (Exception)
        {
            return false;
        }

        return target.Length > 0;
    }

    private static List<string> BuildTargetCandidates(LinkFileContent content, string newTarget)
    {
        var candidates = new List<string> { newTarget };

        if (content.Targets != null)
        {
            foreach (var previous in content.Targets)
            {
                AppendCandidate(candidates, previous);
            }
        }

        AppendCandidate(candidates, content.Target);
        return candidates;
    }

    private static void AppendCandidate(List<string> candidates, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidates.Count >= MaxTargetCandidates)
            return;

        var normalized = candidate.ToForwardSlashes().TrimEnd('/');
        foreach (var existing in candidates)
        {
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                return;
        }

        candidates.Add(normalized);
    }

    private static bool IsValidTarget(string targetRoot, IResourcePackage package, string linkFilePath)
    {
        var problem = GetTargetProblem(targetRoot, package);
        if (problem == null)
            return true;

        Log.Warning($"Refusing link {linkFilePath} -> {targetRoot}: {problem}");
        return false;
    }

    private static string? GetTargetProblem(string targetRoot, IResourcePackage package)
    {
        var assetsFolder = package.AssetsFolder;

        if (assetsFolder.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            return "This folder contains the project's assets folder.";

        if (targetRoot.StartsWith(assetsFolder, StringComparison.OrdinalIgnoreCase))
            return "This folder is already inside the project's assets folder.";

        return null;
    }

    private const int MaxTargetCandidates = 4;

    private static readonly List<AssetLinkFolder> _mounts = [];
}
