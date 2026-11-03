#nullable enable
using System.IO;
using System.Security.Cryptography;
using System.Text;
using T3.Core.Compilation;

namespace T3.Editor.Compilation;

/// <summary>
/// Records size, write time and content hash of every source file that went into a project's last build, so
/// startup can tell whether the build output is still current without a version number in the csproj.
/// It lives next to the build output, so cleaning or deleting the build removes it as well.
/// </summary>
internal sealed class SourceManifest
{
    private readonly record struct Entry(long Size, long WriteTicks, string Hash);

    /// <summary>
    /// Snapshots the project's sources, reusing the stored hash of every file whose size and write time are unchanged.
    /// Take it before compiling, so an edit made while the build runs is detected at the next start.
    /// </summary>
    public static SourceManifest Capture(CsProjectFile project)
    {
        var buildDirectory = project.GetBuildTargetDirectory(CsProjectFile.EditorBuildMode);
        return Capture(EnumerateSources(project), TryLoad(buildDirectory));
    }

    /// <summary>
    /// Stores the snapshot as the record of the build that now sits in <paramref name="buildDirectory"/>.
    /// </summary>
    public void SaveForBuild(string buildDirectory)
    {
        var packageInfo = new FileInfo(Path.Combine(buildDirectory, ReleaseInfo.FileName));
        if (packageInfo.Exists)
            TrySave(buildDirectory, packageInfo.LastWriteTimeUtc.Ticks);
    }

    /// <summary>
    /// True if the build in <paramref name="buildDirectory"/> was compiled from the current sources. The checks
    /// escalate from the file list to size to write time; only files with a new write time get hashed.
    /// </summary>
    public static bool IsBuildCurrent(CsProjectFile project, string buildDirectory, out string reason)
    {
        var packageInfo = new FileInfo(Path.Combine(buildDirectory, ReleaseInfo.FileName));
        if (!packageInfo.Exists)
        {
            reason = "the build has no package info";
            return false;
        }

        var buildTicks = packageInfo.LastWriteTimeUtc.Ticks;
        var sources = EnumerateSources(project);
        var stored = TryLoad(buildDirectory);

        // IDE and command-line builds write no manifest; trust such a build when it is newer than every source.
        if (stored == null || stored._buildTicks != buildTicks)
        {
            foreach (var (relativePath, file) in sources)
            {
                if (file.LastWriteTimeUtc.Ticks <= buildTicks)
                    continue;

                reason = $"{relativePath} is newer than the build";
                return false;
            }

            Capture(sources, stored).TrySave(buildDirectory, buildTicks);
            reason = string.Empty;
            return true;
        }

        if (sources.Count != stored._entries.Count)
        {
            reason = "source files were added or removed";
            return false;
        }

        var hasNewWriteTimes = false;
        foreach (var (relativePath, file) in sources)
        {
            if (!stored._entries.TryGetValue(relativePath, out var entry))
            {
                reason = $"{relativePath} was added";
                return false;
            }

            if (entry.Size != file.Length)
            {
                reason = $"{relativePath} changed";
                return false;
            }

            var writeTicks = file.LastWriteTimeUtc.Ticks;
            if (entry.WriteTicks == writeTicks)
                continue;

            // Git rewrites files with a new write time even when their content ends up unchanged
            if (TryComputeHash(file) != entry.Hash)
            {
                reason = $"{relativePath} changed";
                return false;
            }

            stored._entries[relativePath] = entry with { WriteTicks = writeTicks };
            hasNewWriteTimes = true;
        }

        if (hasNewWriteTimes)
            stored.TrySave(buildDirectory, buildTicks);

        reason = string.Empty;
        return true;
    }

    private SourceManifest(Dictionary<string, Entry> entries, long buildTicks)
    {
        _entries = entries;
        _buildTicks = buildTicks;
    }

    private static SourceManifest Capture(List<(string RelativePath, FileInfo File)> sources, SourceManifest? previous)
    {
        var entries = new Dictionary<string, Entry>(sources.Count, StringComparer.Ordinal);
        foreach (var (relativePath, file) in sources)
        {
            var size = file.Length;
            var writeTicks = file.LastWriteTimeUtc.Ticks;
            var hash = previous != null
                       && previous._entries.TryGetValue(relativePath, out var known)
                       && known.Size == size
                       && known.WriteTicks == writeTicks
                           ? known.Hash
                           : TryComputeHash(file);

            entries[relativePath] = new Entry(size, writeTicks, hash);
        }

        return new SourceManifest(entries, 0);
    }

    /// <summary>
    /// The files whose content decides the build output: all C# sources below the project folder, the csproj
    /// and its Directory.Build.props. Mirrors the SDK compile glob, which skips dot-folders, bin and obj.
    /// </summary>
    private static List<(string RelativePath, FileInfo File)> EnumerateSources(CsProjectFile project)
    {
        var root = new DirectoryInfo(project.Directory);
        var sources = new List<(string, FileInfo)>(256);

        AddIfExists(sources, root, new FileInfo(project.FullPath));
        AddIfExists(sources, root, new FileInfo(Path.Combine(root.FullName, ProjectXml.BuildPropsFileName)));

        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo subDirectory)
                {
                    var name = subDirectory.Name;
                    if (name.StartsWith('.') || name == "bin" || name == "obj")
                        continue;

                    pending.Push(subDirectory);
                }
                else if (entry is FileInfo file && file.Extension == ".cs")
                {
                    sources.Add((GetRelativePath(root, file), file));
                }
            }
        }

        return sources;

        static void AddIfExists(List<(string, FileInfo)> sources, DirectoryInfo root, FileInfo file)
        {
            if (file.Exists)
                sources.Add((GetRelativePath(root, file), file));
        }

        static string GetRelativePath(DirectoryInfo root, FileInfo file)
        {
            return Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
        }
    }

    /** An unreadable file gets an empty hash, which never matches, so it counts as changed. */
    private static string TryComputeHash(FileInfo file)
    {
        try
        {
            using var stream = file.OpenRead();
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception e)
        {
            Log.Debug($"Could not hash {file.FullName}: {e.Message}");
            return string.Empty;
        }
    }

    private static SourceManifest? TryLoad(string buildDirectory)
    {
        var path = Path.Combine(buildDirectory, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            if (reader.ReadLine() != FormatHeader)
                return null;

            var buildLine = reader.ReadLine();
            if (buildLine == null || !long.TryParse(buildLine, out var buildTicks))
                return null;

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } line)
            {
                var columns = line.Split('\t', 4);
                if (columns.Length != 4
                    || !long.TryParse(columns[1], out var size)
                    || !long.TryParse(columns[2], out var writeTicks))
                    return null;

                entries[columns[3]] = new Entry(size, writeTicks, columns[0]);
            }

            return new SourceManifest(entries, buildTicks);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read source manifest {path}: {e.Message}");
            return null;
        }
    }

    /** A manifest that fails to save only costs a rebuild or a re-hash at the next start. */
    private void TrySave(string buildDirectory, long buildTicks)
    {
        var builder = new StringBuilder(_entries.Count * 120);
        builder.Append(FormatHeader).Append('\n')
               .Append(buildTicks).Append('\n');

        foreach (var (relativePath, entry) in _entries)
        {
            builder.Append(entry.Hash).Append('\t')
                   .Append(entry.Size).Append('\t')
                   .Append(entry.WriteTicks).Append('\t')
                   .Append(relativePath).Append('\n');
        }

        var path = Path.Combine(buildDirectory, FileName);
        try
        {
            File.WriteAllText(path, builder.ToString());
        }
        catch (Exception e)
        {
            Log.Debug($"Could not write source manifest {path}: {e.Message}");
        }
    }

    private const string FileName = "SourceManifest.txt";
    private const string FormatHeader = "tixl-source-manifest 1";

    private readonly Dictionary<string, Entry> _entries;

    /** Write time of the package info of the build this manifest describes; ties the manifest to one build. */
    private readonly long _buildTicks;
}
