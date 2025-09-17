#nullable enable
using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Compilation;

/// <summary>
/// The class that executes runtime compilation commands to build a csproj file via the dotnet CLI
/// </summary>
internal static class Compiler
{
    private static readonly string _workingDirectory = Path.Combine(T3.Core.UserData.FileLocations.TempFolder, "CompilationWorkingDirectory");

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
        var psi = new ProcessStartInfo
                      {
                          FileName = "cmd.exe",
                          Arguments = $"/C {commandLine}",
                          RedirectStandardOutput = true,
                          RedirectStandardError = true,
                          UseShellExecute = false,
                          CreateNoWindow = true,
                          WorkingDirectory = workingDirectory
                      };

        var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
                                      {
                                          if (e.Data != null) outputBuilder.AppendLine(e.Data);
                                      };
        process.ErrorDataReceived += (_, e) =>
                                     {
                                         if (e.Data != null) outputBuilder.AppendLine(e.Data);
                                     };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

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
        var verbosity = UserSettings.Config?.CompileCsVerbosity ?? CompilerOptions.Verbosity.Normal;
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
                 .Append(" --nologo");

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