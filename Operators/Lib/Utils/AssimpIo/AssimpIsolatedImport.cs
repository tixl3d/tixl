#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using T3.Core.Utils;

namespace Lib.Utils.AssimpIo;

/// <summary>
/// Runs Assimp imports and exports in a separate process, so a native crash or hang inside Assimp
/// can't take the editor down: a crash becomes an error status and a hang is killed when the
/// operation is cancelled or times out.
/// </summary>
/// <remarks>
/// Inside TiXL the child is the editor executable itself, started with <c>--assimp-host</c>; it never
/// boots the UI. That needs no dotnet installation, so self-contained release builds stay isolated.
/// Outside an editor (tests, hosts) the small AssimpImportHost launcher shipped in this package's
/// output is used with a <c>dotnet</c> executable; if neither is available, callers run in-process.
/// </remarks>
internal static class AssimpIsolatedImport
{
    private readonly record struct HostCommand(string Executable, IReadOnlyList<string> PrefixArguments);

    /// <summary>Whether a child process can be started. Probed once per session; blocks up to a few seconds on first use.</summary>
    public static bool IsAvailable => _availability.Value;

    /// <summary>Why <see cref="IsAvailable"/> is false; empty while it is true.</summary>
    public static string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>Imports in a child process. Throws on import errors, crashes and timeouts; honours cancellation by killing the child.</summary>
    public static AssimpModelData Load(string absolutePath, in AssimpImportSettings settings, bool reusePreviousMaterials, CancellationToken token)
    {
        var outputPath = CreateTempFilePath();
        try
        {
            var childArguments = new List<string>
                                     {
                                         ImportArgument,
                                         absolutePath,
                                         outputPath,
                                         ((int)settings.Preset).ToString(CultureInfo.InvariantCulture),
                                         settings.Scale.ToString("R", CultureInfo.InvariantCulture),
                                         settings.Normalize ? "1" : "0",
                                         ((int)settings.AxisConversion).ToString(CultureInfo.InvariantCulture),
                                         settings.FlipNormals ? "1" : "0",
                                         settings.SelectedMeshIndex.ToString(CultureInfo.InvariantCulture),
                                         settings.ImportMaterials ? "1" : "0",
                                         reusePreviousMaterials ? "1" : "0",
                                     };

            var (exitCode, errorOutput) = RunHost(childArguments, MaxOperationDuration, token);
            switch (exitCode)
            {
                case ExitSuccess:
                    break;
                case ExitOperationFailed:
                    throw new InvalidDataException(errorOutput.Length > 0 ? errorOutput : "Import failed");
                default:
                    throw new InvalidOperationException($"The import process crashed (exit code {exitCode}). "
                                                        + "The file is probably damaged or uses features Assimp can't handle."
                                                        + (errorOutput.Length > 0 ? $"\n{errorOutput}" : string.Empty));
            }

            using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            return AssimpModelDataSerializer.Read(stream);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    /// <summary>
    /// Exports in a child process. The snapshot is written to a temp file and read by the child, so
    /// the worker only hands over plain arrays. Throws on errors, crashes and timeouts and removes a
    /// partially written output file; honours cancellation by killing the child.
    /// </summary>
    public static void Export(AssimpModelData data, string absolutePath, string formatId, string meshName, CancellationToken token)
    {
        var dataPath = CreateTempFilePath();
        try
        {
            using (var stream = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                AssimpModelDataSerializer.Write(stream, data);
            }

            var childArguments = new[] { ExportArgument, dataPath, absolutePath, formatId, meshName };
            var (exitCode, errorOutput) = RunHost(childArguments, MaxOperationDuration, token);
            if (exitCode == ExitSuccess)
                return;

            // The caller reserves a name that doesn't exist yet, so a file here is a broken partial result
            TryDelete(absolutePath);
            if (exitCode == ExitOperationFailed)
                throw new IOException(errorOutput.Length > 0 ? errorOutput : "Export failed");

            throw new InvalidOperationException($"The export process crashed (exit code {exitCode})."
                                                + (errorOutput.Length > 0 ? $"\n{errorOutput}" : string.Empty));
        }
        catch
        {
            TryDelete(absolutePath);
            throw;
        }
        finally
        {
            TryDelete(dataPath);
        }
    }

    /// <summary>
    /// Entry point inside the child process, called by the launcher. Arguments as built in
    /// <see cref="Load"/> and <see cref="Export"/>, or "--probe" to check that the child can start at all.
    /// </summary>
    public static int RunChild(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == ProbeArgument)
        {
            Console.Out.Write(ProbeResponse);
            return ExitSuccess;
        }

        try
        {
            if (arguments.Length == 0)
                throw new ArgumentException("No operation given");

            return arguments[0] switch
                       {
                           ImportArgument => RunChildImport(arguments[1..]),
                           ExportArgument => RunChildExport(arguments[1..]),
                           _              => throw new ArgumentException($"Unknown operation '{arguments[0]}'"),
                       };
        }
        catch (Exception e)
        {
            Console.Error.Write(e.Message);
            return ExitOperationFailed;
        }
    }

    private static int RunChildImport(string[] arguments)
    {
        if (arguments.Length != 10)
            throw new ArgumentException($"Expected 10 import arguments, got {arguments.Length}");

        var absolutePath = arguments[0];
        var outputPath = arguments[1];
        var settings = new AssimpImportSettings((AssimpPostProcessPresets)int.Parse(arguments[2], CultureInfo.InvariantCulture),
                                                float.Parse(arguments[3], CultureInfo.InvariantCulture),
                                                arguments[4] == "1",
                                                (AssimpAxisConversions)int.Parse(arguments[5], CultureInfo.InvariantCulture),
                                                arguments[6] == "1",
                                                int.Parse(arguments[7], CultureInfo.InvariantCulture),
                                                arguments[8] == "1");
        var reusePreviousMaterials = arguments[9] == "1";
        ExitWhenParentGoes();

        var scenes = AssimpModelLoader.Import(absolutePath, settings.Preset, CancellationToken.None);
        var data = AssimpModelLoader.Convert(scenes, settings, reusePreviousMaterials, CancellationToken.None);

        // Written under a temporary name, so the parent never reads a half-written result
        var partialPath = outputPath + ".partial";
        using (var stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            AssimpModelDataSerializer.Write(stream, data);
        }

        File.Move(partialPath, outputPath, overwrite: true);
        return ExitSuccess;
    }

    private static int RunChildExport(string[] arguments)
    {
        if (arguments.Length != 4)
            throw new ArgumentException($"Expected 4 export arguments, got {arguments.Length}");

        var dataPath = arguments[0];
        var outputPath = arguments[1];
        var formatId = arguments[2];
        var meshName = arguments[3];
        ExitWhenParentGoes();

        AssimpModelData data;
        using (var stream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
        {
            data = AssimpModelDataSerializer.Read(stream);
        }

        AssimpModelExporter.Export(data, outputPath, formatId, meshName);
        return ExitSuccess;
    }

    #region parent side
    private static bool Probe()
    {
        try
        {
            if (!TryGetHostCommand(out var hostCommand, out var failureReason))
            {
                UnavailableReason = failureReason;
                return false;
            }

            var (exitCode, errorOutput, standardOutput) = RunHostCapturingOutput(hostCommand, [ProbeArgument], ProbeTimeout, CancellationToken.None);
            if (exitCode == ExitSuccess && standardOutput == ProbeResponse)
                return true;

            UnavailableReason = $"The isolated Assimp host doesn't start (exit code {exitCode}). {errorOutput}".Trim();
            return false;
        }
        catch (Exception e)
        {
            UnavailableReason = $"The isolated Assimp host doesn't start: {e.Message}";
            return false;
        }
    }

    private static (int ExitCode, string ErrorOutput) RunHost(IReadOnlyList<string> childArguments, TimeSpan timeout, CancellationToken token)
    {
        if (!TryGetHostCommand(out var hostCommand, out var failureReason))
            throw new InvalidOperationException(failureReason);

        var (exitCode, errorOutput, _) = RunHostCapturingOutput(hostCommand, childArguments, timeout, token);
        return (exitCode, errorOutput);
    }

    private static (int ExitCode, string ErrorOutput, string StandardOutput) RunHostCapturingOutput(HostCommand hostCommand,
                                                                                                   IReadOnlyList<string> childArguments,
                                                                                                   TimeSpan timeout,
                                                                                                   CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(hostCommand.Executable)
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true,
                                RedirectStandardOutput = true,

                                // Never written; the child watches it to notice when the editor goes away
                                RedirectStandardInput = true,
                                StandardErrorEncoding = Encoding.UTF8,
                                StandardOutputEncoding = Encoding.UTF8,
                            };

        // Host arguments: the launcher (or the editor's host mode), this assembly, folders to resolve
        // its dependencies from, then the child's own arguments.
        foreach (var argument in hostCommand.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(typeof(AssimpIsolatedImport).Assembly.Location);
        foreach (var folder in GetDependencyFolders())
        {
            startInfo.ArgumentList.Add(folder);
        }

        startInfo.ArgumentList.Add("--");
        foreach (var argument in childArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the import process");
        var errorTask = ReadLimitedAsync(process.StandardError);
        var outputTask = ReadLimitedAsync(process.StandardOutput);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(timeout);
        try
        {
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"The operation didn't finish within {timeout.TotalMinutes:0} minutes and was stopped");
        }

        return (process.ExitCode, errorTask.GetAwaiter().GetResult().Trim(), outputTask.GetAwaiter().GetResult().Trim());
    }

    /** Keeps a chatty or broken child from filling memory through its output. */
    private static async Task<string> ReadLimitedAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            if (builder.Length < MaxCapturedOutputLength)
                builder.Append(buffer, 0, Math.Min(count, MaxCapturedOutputLength - builder.Length));
        }

        return builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone
        }
    }

    /// <summary>
    /// Picks how the worker is started: the editor executable itself when the operator runs inside
    /// TiXL (no dotnet dependency, works in self-contained builds), otherwise the standalone launcher
    /// next to this package plus a dotnet executable.
    /// </summary>
    private static bool TryGetHostCommand(out HostCommand command, out string failureReason)
    {
        command = default;
        failureReason = string.Empty;

        if (TryGetEditorHostCommand(out var editorExecutable, out var editorArguments))
        {
            command = new HostCommand(editorExecutable, editorArguments);
            return true;
        }

        var hostAssembly = FindHostAssembly();
        if (hostAssembly == null)
        {
            failureReason = "The isolated import host is missing from the package";
            return false;
        }

        var dotnet = FindDotnetExecutable();
        if (dotnet == null)
        {
            failureReason = "No dotnet executable found and the editor can't host the process itself";
            return false;
        }

        command = new HostCommand(dotnet, [hostAssembly]);
        return true;
    }

    /// <summary>The editor's entry point handles <see cref="IsolatedAssimpHost.Flag"/> before initializing anything.</summary>
    private static bool TryGetEditorHostCommand(out string executable, out List<string> prefixArguments)
    {
        executable = string.Empty;
        prefixArguments = [];

        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly?.GetName().Name != "TiXL")
            return false;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            return false;

        // A framework-dependent editor was started as "dotnet TiXL.dll".
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryPath = entryAssembly.Location;
            if (string.IsNullOrEmpty(entryPath) || !File.Exists(entryPath))
                return false;

            prefixArguments.Add(entryPath);
        }

        executable = processPath;
        prefixArguments.Add(IsolatedAssimpHost.Flag);
        return true;
    }

    private static string? FindHostAssembly()
    {
        var packageFolder = Path.GetDirectoryName(typeof(AssimpIsolatedImport).Assembly.Location);
        if (string.IsNullOrEmpty(packageFolder))
            return null;

        var path = Path.Combine(packageFolder, HostFolderName, HostAssemblyName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Folders holding this package's dependencies: Core and the other editor assemblies, and this package itself.</summary>
    private static IEnumerable<string> GetDependencyFolders()
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in (ReadOnlySpan<System.Reflection.Assembly>)[typeof(AssimpIsolatedImport).Assembly, typeof(MeshGeometry).Assembly, typeof(Log).Assembly])
        {
            var folder = Path.GetDirectoryName(assembly.Location);
            if (!string.IsNullOrEmpty(folder))
                folders.Add(folder);
        }

        folders.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return folders;
    }

    private static string? FindDotnetExecutable()
    {
        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        var candidates = new List<string?>
                             {
                                 Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
                                 Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath : null,
                             };

        // A framework-dependent runtime lives in <root>/shared/Microsoft.NETCore.App/<version>/
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        if (!string.IsNullOrEmpty(runtimeDirectory))
            candidates.Add(Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", fileName)));

        foreach (var variable in (ReadOnlySpan<string>)["DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64"])
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(root))
                candidates.Add(Path.Combine(root, fileName));
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(folder.Trim('"'), fileName));
        }

        candidates.Add(OperatingSystem.IsWindows()
                           ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", fileName)
                           : OperatingSystem.IsMacOS()
                               ? "/usr/local/share/dotnet/dotnet"
                               : "/usr/lib/dotnet/dotnet");

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string CreateTempFilePath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tixl-assimp");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{Guid.NewGuid():N}.bin");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            File.Delete(path + ".partial");
        }
        catch (Exception)
        {
            // Temp files are cleaned up by the OS eventually
        }
    }
    #endregion

    /// <summary>
    /// Ends the child when the editor goes away mid-operation, instead of leaving it running. The
    /// parent holds the child's stdin open and never writes to it, so end-of-stream means the parent
    /// is gone, however it ended. Works the same on every platform, unlike watching a process id.
    /// </summary>
    private static void ExitWhenParentGoes()
    {
        var watcher = new Thread(() =>
                                 {
                                     try
                                     {
                                         using var input = Console.OpenStandardInput();
                                         var buffer = new byte[64];
                                         while (input.Read(buffer, 0, buffer.Length) > 0)
                                         {
                                         }
                                     }
                                     catch (Exception)
                                     {
                                         // A broken pipe also means the parent is gone
                                     }

                                     Environment.Exit(ExitParentGone);
                                 })
                          {
                              IsBackground = true,
                              Name = "AssimpImportParentWatch",
                          };
        watcher.Start();
    }

    private static readonly Lazy<bool> _availability = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    private const string HostFolderName = "AssimpImportHost";
    private const string HostAssemblyName = "AssimpImportHost.dll";
    private const string ProbeArgument = "--probe";
    private const string ImportArgument = "--import";
    private const string ExportArgument = "--export";
    private const string ProbeResponse = "assimp-import-host-ok";

    private const int ExitSuccess = 0;
    private const int ExitOperationFailed = 2;
    private const int ExitParentGone = 3;
    private const int MaxCapturedOutputLength = 16 * 1024;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxOperationDuration = TimeSpan.FromMinutes(10);
}
