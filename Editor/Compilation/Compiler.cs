#nullable enable
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Text;
using Main;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Compilation;

/// <summary>
/// The class that executes runtime compilation commands to build a csproj file via the dotnet CLI
/// </summary>
internal static class Compiler
{
    internal static void StopProcess()
    {
        _processLock.Enter();
        if (_processCommander == null)
            return;
            
        _processCommander.Close();
        _processCommander = null;
        _processLock.Exit();
    }
    
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
    
    private static ProcessCommander<CompilationOptions>? _processCommander;
    private static readonly System.Threading.Lock _processLock = new();
    private static readonly Stopwatch _stopwatch = new();

    internal static bool TryCompile(CsProjectFile projectFile, BuildMode buildMode, bool nugetRestore)
    {
        var verbosity = CompilerOptions.Verbosity.Normal;
        if (UserSettings.Config != null)
        {
            verbosity = UserSettings.Config.CompileCsVerbosity; 
        }
        
        bool success;
        string logMessage;
        _processLock.Enter();
        _stopwatch.Restart();
        if (_processCommander == null)
        {
            _processCommander = new ProcessCommander<CompilationOptions>(_workingDirectory, "Compilation: ");
            //Log.Debug("Compilation process started");
        }
            
        if (!_processCommander.TryBeginProcess(out var isRunning) && !isRunning)
        {
            Log.Error("Failed to start compilation process");
            return false;
        }
            
        Log.Debug($"Compiling {projectFile.Name} in {buildMode} mode");

        var compilationOptions = new CompilationOptions(projectFile, buildMode, verbosity, nugetRestore);
        var command = new Command<CompilationOptions>(GetCommandFor, Evaluate);

        var noOutput = UserSettings.Config == null || UserSettings.Config.LogCsCompilationDetails==false;
        if (!_processCommander.TryCommand(command, compilationOptions, out var response, projectFile.Directory, suppressOutput: noOutput))
        {
            success = false;
            logMessage = $"{projectFile.Name}: Build failed in {_stopwatch.ElapsedMilliseconds}ms:\n{response}";
            foreach (var line in response.Split('\n'))
            {
                var warningIndex = line.IndexOf("error CS", StringComparison.Ordinal);
                if (warningIndex != -1)
                {
                    Log.Warning(line.Substring(warningIndex,line.Length -warningIndex));
                }
            }
        }
        else
        {
            success = true;
            logMessage = $"{projectFile.Name}: Build succeeded in {_stopwatch.ElapsedMilliseconds}ms";
        }
        
        _processLock.Exit();

        if (!success)
        {
            Log.Error(logMessage);
        }
        else
        {
            Log.Info(logMessage);
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
    public enum Verbosity { Quiet, Minimal, Normal, Detailed, Diagnostic }
}