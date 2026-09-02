using System.Diagnostics;
using TiXL.DebugClient;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Editor.IntegrationTests;

/// <summary>
/// Shared editor for all integration tests: attaches to an already-running editor
/// (TIXL_DEBUG_PORT, default 9042) or launches one itself and shuts it down at the end.
/// Tests run serialized (one shared editor, one protocol connection).
/// </summary>
public sealed class EditorFixture : IDisposable
{
    public DebugProtocolClient Client { get; }

    /// <summary>The _agentTests playground - empty, hot-reloadable, outside the repo.</summary>
    public const string PlaygroundProject = "_agentTests";

    public EditorFixture()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("TIXL_DEBUG_PORT"), out var p) ? p : 9042;

        if (DebugProtocolClient.TryConnect(port, TimeSpan.FromSeconds(3), out var client))
        {
            Client = client!;
            return;
        }

        var exePath = FindEditorExe();
        _editorProcess = Process.Start(new ProcessStartInfo(exePath, $"--debug-server {port} --window 1280x720 --no-splash")
                                           {
                                               WorkingDirectory = Path.GetDirectoryName(exePath)!,
                                               UseShellExecute = false,
                                           })
                         ?? throw new InvalidOperationException("Failed to start TiXL.exe");

        if (!DebugProtocolClient.TryConnect(port, TimeSpan.FromMinutes(2), out client))
        {
            _editorProcess.Kill();
            throw new InvalidOperationException($"Editor did not open debug port {port} within 2 minutes");
        }

        Client = client!;
        Client.PumpFrames(30); // let startup settle
    }

    /// <summary>Opens the playground and returns its baseline child/connection counts.</summary>
    public (int Children, int Connections) OpenPlayground()
    {
        Client.OpenProject(PlaygroundProject).Require("openProject " + PlaygroundProject);
        Client.PumpFrames(10);
        var state = Client.GetGraphState();
        return (state["children"]!.Count(), state["connections"]!.Count());
    }

    /// <summary>Locates the _agentTests source folder (for reload tests), tolerant of the TiXL version folder name.</summary>
    public static string FindPlaygroundSourceFile()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var versionDir in Directory.GetDirectories(documents, "TiXL*"))
        {
            var candidate = Path.Combine(versionDir, "_agentTests", "Symbols", "_agentTests.cs");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Can't locate _agentTests/Symbols/_agentTests.cs under Documents/TiXL*");
    }

    private static string FindEditorExe()
    {
        if (Environment.GetEnvironmentVariable("TIXL_EXE") is { } fromEnv && File.Exists(fromEnv))
            return fromEnv;

        // Walk up from the test assembly to the repo root, then into the editor's build output.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Editor", "bin", "Debug", "net10.0-windows", "TiXL.exe");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Can't locate TiXL.exe - set TIXL_EXE or build Editor first");
    }

    public void Dispose()
    {
        if (_editorProcess == null)
        {
            Client.Dispose();
            return; // attached to someone else's editor - leave it running
        }

        Client.Shutdown();
        Client.Dispose();
        if (!_editorProcess.WaitForExit(30_000))
            _editorProcess.Kill();
    }

    private readonly Process? _editorProcess;
}

[CollectionDefinition("Editor")]
public sealed class EditorCollection : ICollectionFixture<EditorFixture>;
