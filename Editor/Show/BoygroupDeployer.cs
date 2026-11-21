#nullable enable
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Editor.Collab;

namespace T3.Editor.Show;

/// <summary>
/// Project file deployment for boygrouping: the server inventories its project folder, diffs against
/// each client's inventory and pushes only changed files as base64 chunks over the framed session.
/// Clients write to their project root atomically (temp + move) and verify with a per-file hash.
/// Excludes anything machine-local or transient: outputs.machine.json (per-machine display bindings
/// must never be overwritten), .temp build folders, backups and .git.
/// </summary>
internal static class BoygroupDeployer
{
    public const int ChunkSize = 192 * 1024; // base64 inflates by ~1.33; stays well under the frame cap

    private static readonly string[] ExcludedDirectories = [".temp", ".git", "backup", "Backups"];
    private static readonly string[] ExcludedFiles = ["outputs.machine.json", "userSettings.json"];

    /// <summary>
    /// Path comparison for inventories: Windows filesystems are case-insensitive, Linux/macOS are
    /// case-sensitive. Deployed files are written with the server's exact casing, so ordinal
    /// comparison stays consistent on every platform (a client whose pre-existing file only differs
    /// by casing gets it re-sent once and normalized).
    /// </summary>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public sealed class InventoryEntry
    {
        public string RelativePath = "";
        public long Size;
        public long LastWriteTicks;

        /// <summary>Content hash, computed for small project files only (large media would make a
        /// join slow). Lets the diff recognize identical content whose mtime moved — a client that
        /// rewrites an unchanged project file on launch must not be re-sent and restarted forever.</summary>
        public string? Hash;
    }

    private const long MaxHashedBytes = 8 * 1024 * 1024;

    /// <summary>relPath → (size, mtime). Fast compare, same quick-check model as rsync. Files under
    /// <paramref name="excludedRelativeDir"/> (slash-separated, relative to the project root) are left
    /// out: the output setups travel as live <c>setupJson</c> messages instead, and a client that
    /// re-serializes its applied setup would otherwise diff against the master's file forever.</summary>
    public static Dictionary<string, InventoryEntry> BuildInventory(string projectFolder, string? excludedRelativeDir = null)
    {
        var inventory = new Dictionary<string, InventoryEntry>(PathComparer);
        if (string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder))
            return inventory;

        foreach (var filePath in EnumerateProjectFiles(projectFolder, excludedRelativeDir))
        {
            try
            {
                var info = new FileInfo(filePath);
                var relative = Path.GetRelativePath(projectFolder, filePath).Replace('\\', '/');
                inventory[relative] = new InventoryEntry
                                          {
                                              RelativePath = relative,
                                              Size = info.Length,
                                              LastWriteTicks = info.LastWriteTimeUtc.Ticks,
                                              Hash = info.Length <= MaxHashedBytes ? ComputeHash(filePath) : null,
                                          };
            }
            catch (IOException)
            {
                // File locked or vanished mid-walk; skipped this round.
            }
        }

        return inventory;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string projectFolder, string? excludedRelativeDir)
    {
        var excludedPrefix = string.IsNullOrEmpty(excludedRelativeDir)
                                 ? null
                                 : excludedRelativeDir.Replace('\\', '/').TrimEnd('/') + "/";
        var projectPrefix = projectFolder.Replace('\\', '/').TrimEnd('/') + "/";

        // Enumerate depth-first so the exclusion check is per directory segment; obj/bin build
        // outputs never deploy either.
        var stack = new Stack<string>();
        stack.Push(projectFolder);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (directory != projectFolder &&
                (ExcludedDirectories.Contains(directoryName, StringComparer.OrdinalIgnoreCase) || directoryName is "obj" or "bin"))
                continue;

            var directoryRelative = directory.Replace('\\', '/').TrimEnd('/');
            if (excludedPrefix != null && (directoryRelative + "/").StartsWith(projectPrefix + excludedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (ExcludedFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                    continue;

                yield return file;
            }

            foreach (var subDirectory in Directory.EnumerateDirectories(directory))
            {
                // Skip symlinked directories (reparse points): Unix projects can symlink into the
                // parent tree, which would loop the walk forever.
                try
                {
                    if (File.GetAttributes(subDirectory).HasFlag(FileAttributes.ReparsePoint))
                        continue;
                }
                catch (IOException)
                {
                    continue;
                }

                stack.Push(subDirectory);
            }
        }
    }

    private static string? ComputeHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null; // Locked or vanished mid-inventory; the mtime path still decides.
        }
    }

    public static JObject BuildInventoryMessage(Dictionary<string, InventoryEntry> inventory)
    {
        var files = new JObject();
        foreach (var (relativePath, entry) in inventory)
        {
            var fileEntry = new JObject
                                {
                                    ["s"] = entry.Size,
                                    ["m"] = entry.LastWriteTicks,
                                };
            if (entry.Hash != null)
                fileEntry["h"] = entry.Hash;

            files[relativePath] = fileEntry;
        }

        return new JObject
                   {
                       ["t"] = "inventory",
                       ["files"] = files,
                   };
    }

    public static Dictionary<string, InventoryEntry> ParseInventory(JObject message)
    {
        var inventory = new Dictionary<string, InventoryEntry>(PathComparer);
        if (message["files"] is not JObject files)
            return inventory;

        foreach (var (relativePath, token) in files)
        {
            if (token is not JObject entry)
                continue;

            inventory[relativePath] = new InventoryEntry
                                          {
                                              RelativePath = relativePath,
                                              Size = CollabProtocol.TryGetLong(entry, "s", out var size) ? size : 0,
                                              LastWriteTicks = CollabProtocol.TryGetLong(entry, "m", out var lastWrite) ? lastWrite : 0,
                                              Hash = CollabProtocol.GetString(entry, "h"),
                                          };
        }

        return inventory;
    }

    /// <summary>Timestamp comparison slack: a write time round-tripped through another filesystem
    /// (NTFS 100 ns vs ext4 ns) does not reproduce the master's ticks bit-exactly, so exact
    /// equality would re-send every transferred file on every deploy.</summary>
    public static readonly long MtimeToleranceTicks = TimeSpan.FromSeconds(2).Ticks;

    /// <summary>Files the client needs, in stable order (smallest first so most files land early).</summary>
    public static List<string> ComputeMissingFiles(Dictionary<string, InventoryEntry> serverInventory,
                                                   Dictionary<string, InventoryEntry> clientInventory)
    {
        var missing = new List<string>();
        foreach (var (relativePath, serverEntry) in serverInventory)
        {
            if (!clientInventory.TryGetValue(relativePath, out var clientEntry)
                || clientEntry.Size != serverEntry.Size)
            {
                missing.Add(relativePath);
                continue;
            }

            if (Math.Abs(clientEntry.LastWriteTicks - serverEntry.LastWriteTicks) <= MtimeToleranceTicks)
                continue; // Same size, same time: current.

            // Same size, moved time: the content decides. A render node rewrites an unchanged
            // project file on launch (migrations, re-save), which moved its mtime; re-sending it
            // would restart the node every cycle. Both sides hash small files, so identical
            // content is provably current.
            if (clientEntry.Hash != null && serverEntry.Hash != null)
            {
                if (!string.Equals(clientEntry.Hash, serverEntry.Hash, StringComparison.OrdinalIgnoreCase))
                    missing.Add(relativePath);

                continue;
            }

            missing.Add(relativePath);
        }

        missing.Sort((a, b) =>
                         {
                             var sizeCompare = serverInventory[a].Size.CompareTo(serverInventory[b].Size);
                             return sizeCompare != 0 ? sizeCompare : string.CompareOrdinal(a, b);
                         });
        return missing;
    }

    public static List<string> ComputeExtraFiles(Dictionary<string, InventoryEntry> serverInventory,
                                                 Dictionary<string, InventoryEntry> clientInventory)
    {
        return clientInventory.Keys.Where(relativePath => !serverInventory.ContainsKey(relativePath)).ToList();
    }

    // ---------------------------------------------------------------------------------------
    // Server-side streaming of one file
    // ---------------------------------------------------------------------------------------

    public sealed class FileSender
    {
        public readonly string RelativePath;
        public readonly long TotalBytes;
        public readonly long LastWriteTicks;
        public long BytesSent;
        public bool Completed;

        private FileStream? _fileStream;
        private byte[] _buffer = new byte[ChunkSize];
        private SHA256 _sha = SHA256.Create();

        public FileSender(string projectFolder, string relativePath)
        {
            RelativePath = relativePath;
            var fullPath = SafeProjectPath(projectFolder, relativePath);
            var info = new FileInfo(fullPath);
            TotalBytes = info.Length;
            // Shipped with the transfer so the client restores it: the inventory diff compares
            // timestamps, and without this every deploy would re-send every file forever.
            LastWriteTicks = info.LastWriteTimeUtc.Ticks;
            _fileStream = File.OpenRead(fullPath);
        }

        /// <summary>Builds the next chunk message, or the completion message with the content hash.</summary>
        public JObject? NextMessage()
        {
            if (_fileStream == null || Completed)
                return null;

            var read = _fileStream.Read(_buffer, 0, _buffer.Length);
            if (read <= 0)
            {
                // Stream finished: send the hash and close out.
                _sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var hash = Convert.ToHexString(_sha.Hash ?? []);
                Dispose();
                Completed = true;
                return new JObject
                           {
                               ["t"] = "fileEnd",
                               ["path"] = RelativePath,
                               ["hash"] = hash,
                               ["m"] = LastWriteTicks,
                           };
            }

            _sha.TransformBlock(_buffer, 0, read, null, 0);
            BytesSent += read;
            return new JObject
                       {
                           ["t"] = "fileChunk",
                           ["path"] = RelativePath,
                           ["data"] = Convert.ToBase64String(_buffer, 0, read),
                       };
        }

        public void Dispose()
        {
            _fileStream?.Dispose();
            _fileStream = null;
            _sha.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Client-side receiving
    // ---------------------------------------------------------------------------------------

    public sealed class FileReceiver
    {
        public string RelativePath = "";
        public long BytesReceived;
        public string? Error;

        private FileStream? _fileStream;
        private string? _tempPath;
        private SHA256? _sha = SHA256.Create();
        private string _projectFolder = "";

        public void Begin(string projectFolder, JObject message)
        {
            Cancel();
            _sha = SHA256.Create();
            _projectFolder = projectFolder;
            RelativePath = BoygroupDeployer.SafeRelativePath(message);
            var fullPath = SafeProjectPath(projectFolder, RelativePath);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _tempPath = fullPath + ".boygroup-tmp";
            _fileStream = File.Create(_tempPath);
        }

        public void Chunk(JObject message)
        {
            if (_fileStream == null || _sha == null)
                return;

            try
            {
                var data = Convert.FromBase64String(message["data"]?.Value<string>() ?? "");
                _fileStream.Write(data);
                _sha.TransformBlock(data, 0, data.Length, null, 0);
                BytesReceived += data.Length;
            }
            catch (Exception e) when (e is FormatException or IOException)
            {
                // A malformed chunk or a disk write error must fail this file instead of
                // throwing into the UI thread that drains the session's inbound queue.
                Error = $"{RelativePath}: {e.Message}";
                Cancel();
            }
        }

        /// <summary>Verifies the received stream and moves it into place. Returns an error or null.</summary>
        public string? Finish(JObject message)
        {
            try
            {
                if (_sha == null || _tempPath == null)
                    return "no transfer in progress";

                _sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var expectedHash = message["hash"]?.Value<string>() ?? "";
                var actualHash = Convert.ToHexString(_sha.Hash ?? []);
                _fileStream?.Dispose();
                _fileStream = null;

                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (_tempPath != null && File.Exists(_tempPath))
                        File.Delete(_tempPath);

                    Error = $"hash mismatch for {RelativePath}";
                    return Error;
                }

                var fullPath = SafeProjectPath(_projectFolder, RelativePath);
                if (File.Exists(fullPath))
                {
                    // Old file may be read-only or open by a watcher; a replace attempt surfaces that
                    // as the deploy error instead of silently corrupting.
                    File.SetAttributes(fullPath, FileAttributes.Normal);
                    File.Delete(fullPath);
                }

                File.Move(_tempPath!, fullPath);

                // Restore the server's write time so the next inventory diff sees the file as equal
                // (SetLastWriteTimeUtc is supported on all platforms).
                var masterTicks = message["m"]?.Value<long>() ?? 0;
                if (masterTicks != 0)
                    File.SetLastWriteTimeUtc(fullPath, new DateTime(masterTicks, DateTimeKind.Utc));

                return null;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return Error;
            }
            finally
            {
                Dispose();
            }
        }

        public void Cancel()
        {
            _fileStream?.Dispose();
            _fileStream = null;
            if (_tempPath != null && File.Exists(_tempPath))
            {
                try
                {
                    File.Delete(_tempPath);
                }
                catch
                {
                    // Temp cleanup is best-effort.
                }
            }

            Dispose();
        }

        private void Dispose()
        {
            _sha?.Dispose();
            _sha = null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Path safety
    // ---------------------------------------------------------------------------------------

    /// <summary>Resolves a wire-supplied relative path inside the project folder, rejecting escapes.</summary>
    public static string SafeProjectPath(string projectFolder, string relativePath)
    {
        var cleaned = (relativePath ?? "").Replace('\\', '/');
        if (cleaned.Length == 0 || Path.IsPathRooted(cleaned))
            throw new IOException($"Refusing path outside project: '{relativePath}'");

        var root = Path.GetFullPath(projectFolder);
        var fullPath = Path.GetFullPath(Path.Combine(root, cleaned));

        // Compare against the root *including* its separator. A plain prefix check lets a sibling
        // directory whose name starts with the root's name ("proj-evil" next to "proj") pass as
        // inside the project, which is exactly what an absolute or climbing path can name.
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(prefix, comparison))
            throw new IOException($"Refusing path outside project: {relativePath}");

        RejectSymlinkedAncestors(fullPath, prefix, comparison);
        return fullPath;
    }

    /// <summary>
    /// Rejects a path that runs through an existing directory symlink. GetFullPath resolves "."
    /// and ".." textually but never follows links, so a link inside the project folder would
    /// otherwise redirect a write outside it. The project root itself may be a link.
    /// </summary>
    private static void RejectSymlinkedAncestors(string fullPath, string rootWithSeparator, StringComparison comparison)
    {
        var directory = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(directory)
               && directory.Length > rootWithSeparator.Length
               && directory.StartsWith(rootWithSeparator, comparison))
        {
            if (Directory.Exists(directory)
                && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing path through a linked directory: {directory}");

            directory = Path.GetDirectoryName(directory);
        }
    }

    private static string SafeRelativePath(JObject message)
    {
        var path = message["path"]?.Value<string>() ?? "";
        if (path.Contains(".."))
            throw new IOException($"Refusing unsafe path: {path}");

        return path;
    }
}
