#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using T3.Core.Logging;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Runs slangc's command line inside the editor, through the libslang that ships beside slangc. A slangc
/// process spends most of its ~160 ms rebuilding Slang's core module; one session kept here pays that once,
/// which brings a typical shader down to ~40 ms and a syntax error to under a millisecond.
/// </summary>
/// <remarks>
/// The request runs in Slang's command-line mode, so the arguments, the written SPIR-V and reflection JSON
/// and the diagnostics are exactly slangc's (the SPIR-V is byte-identical). Set <c>TIXL_SLANG_IN_PROCESS=0</c>
/// to go back to a process per shader, e.g. to rule this path out when chasing a compiler crash.
/// </remarks>
internal static unsafe class SlangLibrary
{
    /// <summary>
    /// Loads the library beside <paramref name="compiler"/> and creates the session on first use. False when
    /// the library or an export is missing, in which case shaders are compiled by the slangc process.
    /// </summary>
    public static bool TryLoad(string compiler)
    {
        lock (_lock)
        {
            if (_loadAttempted)
                return _session != IntPtr.Zero;

            _loadAttempted = true;

            if (Environment.GetEnvironmentVariable("TIXL_SLANG_IN_PROCESS") == "0")
                return false;

            foreach (var candidate in LibraryCandidates(compiler))
            {
                if (!File.Exists(candidate) || !NativeLibrary.TryLoad(candidate, out var library))
                    continue;

                if (!TryBind(library))
                {
                    Log.Warning($"{candidate} lacks an export TiXL needs; compiling shaders with a slangc process instead.");
                    return false;
                }

                _session = _createSession(IntPtr.Zero);
                if (_session == IntPtr.Zero)
                {
                    Log.Warning($"{candidate} could not create a session; compiling shaders with a slangc process instead.");
                    return false;
                }

                Log.Debug($"Compiling shaders in-process with {candidate}");
                return true;
            }

            Log.Debug("libslang not found beside slangc; compiling shaders with a slangc process.");
            return false;
        }
    }

    /// <summary>Compiles as <c>slangc arguments</c> would. Requires a successful <see cref="TryLoad"/>.</summary>
    public static bool Compile(List<string> arguments, out string diagnostics)
    {
        // A global session is not thread-safe; compiles already queue on the shader cache lock anyway.
        lock (_lock)
        {
            var request = _createCompileRequest(_session);
            var nativeArguments = new IntPtr[arguments.Count];
            try
            {
                for (var index = 0; index < nativeArguments.Length; index++)
                {
                    nativeArguments[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
                }

                // Without it the request ignores -o and -reflection-json, which only slangc acts on.
                _setCommandLineCompilerMode(request);

                int result;
                fixed (IntPtr* argv = nativeArguments)
                {
                    result = _processCommandLineArguments(request, argv, nativeArguments.Length);
                }

                if (result >= 0)
                    result = _compile(request);

                diagnostics = Marshal.PtrToStringUTF8(_getDiagnosticOutput(request))?.Trim() ?? string.Empty;
                return result >= 0;
            }
            finally
            {
                foreach (var argument in nativeArguments)
                {
                    Marshal.FreeCoTaskMem(argument);
                }

                _destroyCompileRequest(request);
            }
        }
    }

    /// <summary>
    /// Where a Slang release keeps the library relative to slangc: beside it on Windows, in <c>../lib</c>
    /// elsewhere. A symlinked slangc (distribution packages) is followed to its release folder first.
    /// </summary>
    private static IEnumerable<string> LibraryCandidates(string compiler)
    {
        var resolved = new FileInfo(compiler).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? compiler;
        var binFolder = Path.GetDirectoryName(Path.GetFullPath(resolved));
        if (binFolder == null)
            yield break;

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(binFolder, "slang-compiler.dll");
            yield return Path.Combine(binFolder, "slang.dll");
            yield break;
        }

        var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        foreach (var folder in new[] { Path.Combine(binFolder, "..", "lib"), binFolder })
        {
            yield return Path.Combine(folder, "libslang-compiler" + extension);
            yield return Path.Combine(folder, "libslang" + extension);
        }
    }

    private static bool TryBind(IntPtr library)
    {
        if (!NativeLibrary.TryGetExport(library, "spCreateSession", out var createSession)
            || !NativeLibrary.TryGetExport(library, "spCreateCompileRequest", out var createCompileRequest)
            || !NativeLibrary.TryGetExport(library, "spDestroyCompileRequest", out var destroyCompileRequest)
            || !NativeLibrary.TryGetExport(library, "spProcessCommandLineArguments", out var processCommandLineArguments)
            || !NativeLibrary.TryGetExport(library, "spCompile", out var compile)
            || !NativeLibrary.TryGetExport(library, "spGetDiagnosticOutput", out var getDiagnosticOutput))
            return false;

        // Exported only under its C++ name, which differs between the Itanium and MSVC manglings.
        if (!NativeLibrary.TryGetExport(library, "_Z28spSetCommandLineCompilerModePN5slang15ICompileRequestE", out var setCommandLineCompilerMode)
            && !NativeLibrary.TryGetExport(library, "?spSetCommandLineCompilerMode@@YAXPEAUICompileRequest@slang@@@Z", out setCommandLineCompilerMode))
            return false;

        _createSession = (delegate* unmanaged<IntPtr, IntPtr>)createSession;
        _createCompileRequest = (delegate* unmanaged<IntPtr, IntPtr>)createCompileRequest;
        _destroyCompileRequest = (delegate* unmanaged<IntPtr, void>)destroyCompileRequest;
        _processCommandLineArguments = (delegate* unmanaged<IntPtr, IntPtr*, int, int>)processCommandLineArguments;
        _compile = (delegate* unmanaged<IntPtr, int>)compile;
        _getDiagnosticOutput = (delegate* unmanaged<IntPtr, IntPtr>)getDiagnosticOutput;
        _setCommandLineCompilerMode = (delegate* unmanaged<IntPtr, void>)setCommandLineCompilerMode;
        return true;
    }

    private static readonly object _lock = new();
    private static bool _loadAttempted;

    /** Lives as long as the process: creating it is the cost this class exists to avoid. */
    private static IntPtr _session;

    private static delegate* unmanaged<IntPtr, IntPtr> _createSession;
    private static delegate* unmanaged<IntPtr, IntPtr> _createCompileRequest;
    private static delegate* unmanaged<IntPtr, void> _destroyCompileRequest;
    private static delegate* unmanaged<IntPtr, IntPtr*, int, int> _processCommandLineArguments;
    private static delegate* unmanaged<IntPtr, int> _compile;
    private static delegate* unmanaged<IntPtr, IntPtr> _getDiagnosticOutput;
    private static delegate* unmanaged<IntPtr, void> _setCommandLineCompilerMode;
}
