#nullable enable
using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Compilation;

/// <summary>
/// The class that executes runtime compilation commands to build a csproj file via the dotnet CLI
/// </summary>
internal static class Compiler
{
    private static readonly string _workingDirectory = Path.Combine(T3.Core.Settings.FileLocations.TempFolder, "CompilationWorkingDirectory");

    static Compiler()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    /// <summary>
    /// Returns the string-based command for the given compilation options
    /// </summary>
    private static string GetCommandFor(in CompilationOptions compilationOptions)
    {
        var projectFile = compilationOptions.ProjectFile;

        var buildModeName = compilationOptions.BuildMode == BuildMode.Debug ? "Debug" : "Release";

        var restoreArg = compilationOptions.RestoreNuGet ? "" : "--no-restore";

        // construct command
        const string fmt = "$env:DOTNET_CLI_UI_LANGUAGE=\"en\"; dotnet build '{0}' --nologo --configuration {1} --verbosity {2} {3} " +
                           "--no-dependencies -property:PreferredUILang=en-US";
        return string.Format(fmt, projectFile.FullPath, buildModeName, _verbosityArgs[compilationOptions.Verbosity], restoreArg);
    }

    /// <summary>
    /// Evaluates the output of the compilation process to check if compilation has failed.
    /// Somewhat crude approach, but it works for now.
    /// </summary>
    /// <param name="output">The output of the compilation process. Can be modified here if desired (e.g. to print more useful/succinct information)</param>
    /// <param name="options">The compilation options associated with this execution output</param>
    /// <returns>True if compilation was successful</returns>
    private static bool Evaluate(ref string output, in CompilationOptions options)
    {
        if (output.Contains("Build succeeded")) return true;

        // print only errors
        const string searchTerm = "error";
        var searchTermSpan = searchTerm.AsSpan();
        for (int i = 0; i < output.Length; i++)
        {
            var newlineIndex = output.IndexOf('\n', i);
            var endOfLineIndex = newlineIndex == -1
                                     ? output.Length
                                     : newlineIndex;

            var span = output.AsSpan(i, endOfLineIndex - i);
            // if span contains "error"
            if (span.IndexOf(searchTermSpan) != -1)
            {
                _failureLogSb.Append(span).AppendLine();
            }

            i = endOfLineIndex;
        }

        output = _failureLogSb.ToString();
        _failureLogSb.Clear();
        return false;
    }

    /// <summary>
    /// The struct that holds the information necessary to create the dotnet build command
    /// </summary>
    private readonly record struct CompilationOptions(CsProjectFile ProjectFile, BuildMode BuildMode, CompilerOptions.Verbosity Verbosity, bool RestoreNuGet);

    private static readonly System.Threading.Lock _processLock = new();

    private static (string Output, int ExitCode) RunCommand(string commandLine, string workingDirectory)
    {
        // Split the 'dotnet' command from its arguments
        var firstSpace = commandLine.IndexOf(' ');
        var fileName = firstSpace == -1 ? commandLine : commandLine.Substring(0, firstSpace);
        var args = firstSpace == -1 ? "" : commandLine.Substring(firstSpace + 1);

        var psi = new ProcessStartInfo
                      {
                          FileName = fileName, // Call "dotnet" directly
                          Arguments = args,
                          RedirectStandardOutput = true,
                          RedirectStandardError = true,
                          UseShellExecute = false,
                          CreateNoWindow = true,
                          WorkingDirectory = workingDirectory
                      };        
        psi.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";
        psi.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        
        var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
                                      {
                                          if (e.Data != null)
                                          {
                                              outputBuilder.AppendLine(e.Data);
                                          }
                                          
                                      };
        process.ErrorDataReceived += (_, e) =>
                                     {
                                         if (e.Data != null)
                                         {
                                             outputBuilder.AppendLine(e.Data);
                                         }
                                     };

        var startTime = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // Emit a marker ExplainBuildFailure recognises so the caller can show a useful hint.
            var marker = e.NativeErrorCode == 2 ? "DOTNET_NOT_FOUND" : "WIN32_EXEC_FAILED";
            return ($"{marker}: {fileName}: {e.Message}", -1);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        
        // 1. Wait for the process to exit via the timeout
        if (!process.WaitForExit(3*60*1000)) 
        {
            Log.Error("Compilation timed out. Force killing process tree...");
            process.Kill(true);
    
            // 2. IMPORTANT: Even after killing, the pipes might be stuck. 
            // We cancel the async reads to unblock the internal stream drains.
            process.CancelOutputRead();
            process.CancelErrorRead();
        }
        else
        {
            // 3. Process exited normally, but we must call the parameterless WaitForExit
            // to ensure the async event buffers are fully drained into your StringBuilder.
            process.WaitForExit(); 
        }

        var time = startTime.Elapsed.Milliseconds * 0.001;
        Log.Debug($" Compiled in {time:0.0}s");

        return (outputBuilder.ToString(), process.ExitCode);
        
    }

    /// <summary>
    /// Attempts to compile the given project file.
    /// </summary>
    /// <param name="projectFile">The project to compile</param>
    /// <param name="buildMode">Building in debug or release mode - debug is for in-progress editable packages, and release is for published packages and players</param>
    /// <param name="nugetRestore">Whether to perform a "dotnet restore" prior to compiling</param>
    /// <param name="output">Contains build process output when compilation fails</param>
    /// <returns></returns>
    internal static bool TryCompile(CsProjectFile projectFile, BuildMode buildMode, bool nugetRestore, [NotNullWhen(false)] out string? output)
    {
        var verbosity = UserSettings.Config?.CompileCsVerbosity ?? CompilerOptions.Verbosity.Minimal;
        output = null;
        
        if (nugetRestore)
        {
            var (restoreOutput, restoreExitCode) = RunCommand($"dotnet restore \"{projectFile.FullPath}\" --nologo", projectFile.Directory);
            output = restoreOutput;
            if (restoreExitCode != 0)
            {
                Log.Error($"Restore failed:\n{restoreOutput}");
                return false;
            }
        }
        
        var arguments = new StringBuilder();

        arguments.Append("dotnet build \"")
                 .Append(projectFile.FullPath)
                 .Append("\" --configuration ")
                 .Append(buildMode)
                 .Append(" --verbosity ")
                 .Append(verbosity.ToString().ToLower())

                 .Append(" --nologo ")
                 .Append(" --no-restore"); // Optimization: Skip restore if you already did it
            

        var stopwatch = Stopwatch.StartNew();
        var (logOutput, exitCode) = RunCommand(arguments.ToString(), projectFile.Directory);

        var success = exitCode == 0;
        var logMessage = success
                             ? $"{projectFile.Name}: Build succeeded in {stopwatch.ElapsedMilliseconds}ms"
                             : $"{projectFile.Name}: Build failed in {stopwatch.ElapsedMilliseconds}ms";

        foreach (var line in logOutput.Split('\n'))
        {
            if (line.Contains("error CS", StringComparison.OrdinalIgnoreCase))
                Log.Warning(line.Trim());
        }

        if (!success)
            Log.Error(logMessage);
        else
            Log.Info(logMessage);

        if (!success)
        {
            output = output == null ? logOutput : $"Restore output: ```\n{output}\n```\n Build output: \n```\n{output}\n```\n";
        }

        return success;
    }

    /// <summary>
    /// Translate a known build/restore failure pattern into a human-readable hint.
    /// Returns null if the failure is not recognized.
    /// </summary>
    public static string? ExplainBuildFailure(string? buildOutput)
    {
        if (string.IsNullOrEmpty(buildOutput))
            return null;

        // DOTNET_NOT_FOUND: synthetic marker emitted by RunCommand when the
        // dotnet executable couldn't be launched (typically because the .NET
        // SDK is not installed, or installed but not yet on PATH).
        if (buildOutput.StartsWith("DOTNET_NOT_FOUND:", StringComparison.Ordinal))
        {
            return "TiXL needs the .NET SDK to compile your projects, but the `dotnet` " +
                   "command was not found in PATH.\n\n" +
                   "Fix: install the .NET SDK from https://dotnet.microsoft.com/download. " +
                   "If you just installed it, sign out and back in (or reboot) so PATH " +
                   "refreshes — the installer doesn't push the change to already-running " +
                   "processes.";
        }

        // NU1100: Unable to resolve '<package>' for '<tfm>'
        // Most common cause: the matching .NET SDK / targeting pack for that TFM
        // is not installed on this machine.
        var nu1100 = _nu1100Regex.Match(buildOutput);
        if (nu1100.Success)
        {
            var pkg = nu1100.Groups[1].Value;
            var tfm = nu1100.Groups[2].Value;
            return $"NuGet could not resolve '{pkg}' for target framework '{tfm}'.\n\n" +
                   $"This usually means the .NET SDK / targeting pack for '{tfm}' " +
                   $"is not installed on this machine.\n\n" +
                   $"Fix: install the matching .NET SDK from https://dotnet.microsoft.com/download " +
                   $"(make sure it includes the targeting pack for '{tfm}'), or update the project's " +
                   $"<TargetFramework> to a TFM you do have installed.";
        }

        return null;
    }

    private static readonly Regex _nu1100Regex = new(
        @"NU1100:\s*Unable to resolve '([^']+)' for '([^']+)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public enum BuildMode
    {
        Debug,
        Release
    }

    private static readonly FrozenDictionary<CompilerOptions.Verbosity, string> _verbosityArgs = new Dictionary<CompilerOptions.Verbosity, string>()
                                                                                                     {
                                                                                                         { CompilerOptions.Verbosity.Quiet, "q" },
                                                                                                         { CompilerOptions.Verbosity.Minimal, "m" },
                                                                                                         { CompilerOptions.Verbosity.Normal, "n" },
                                                                                                         { CompilerOptions.Verbosity.Detailed, "d" },
                                                                                                         { CompilerOptions.Verbosity.Diagnostic, "diag" }
                                                                                                     }.ToFrozenDictionary();

    private static readonly StringBuilder _failureLogSb = new();
}

/** Public interface so options can be used in user settings */
public static class CompilerOptions
{
    public enum Verbosity
    {
        Quiet,
        Minimal,
        Normal,
        Detailed,
        Diagnostic
    }
}