using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// [AssimpImport] and [AssimpExport] in the real editor: the isolated and in-process imports agree,
/// the isolated export writes its file, and the in-process fallback does too. The model comes from
/// the Examples package and the export goes to a temporary folder, so the test needs no fixture
/// assets and leaves nothing behind.
/// </summary>
[Collection("Editor")]
public sealed class AssimpImportExportTests(EditorFixture editor)
{
    private const string ModelAddress = "Examples:test-footage/gltf/gltf-scene-test-01.glb";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void IsolatedAndInProcessImportAndExport_Work()
    {
        var client = editor.Client;
        editor.OpenPlayground();
        var baselineLogSeq = client.GetLatestLogSeq();
        var exportFolder = Path.Combine(Path.GetTempPath(), "tixl-assimp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportFolder);

        var import = Guid.Empty;
        var export = Guid.Empty;
        try
        {
            import = client.AddOp("AssimpImport");
            client.SetInput(import, "FilePath", new JValue(ModelAddress));

            var isolatedMetadata = WaitForOutput(client, import, "Metadata",
                                                 value => value?.Contains("Imported in a separate process") == true);
            var isolatedCounts = ReadCounts(client, import);
            Assert.True(isolatedCounts.Vertices > 0, $"isolated import produced no vertices ({isolatedMetadata})");
            Assert.True(isolatedCounts.Meshes >= 2, $"expected the multi-object test file, got {isolatedCounts.Meshes} meshes");

            // Same file and settings, only the process changes: the converted result must match
            client.SetInput(import, "IsolateImport", new JValue(false));
            var inProcessMetadata = WaitForOutput(client, import, "Metadata",
                                                  value => value?.Contains("Imported in a separate process") == false
                                                           && (value.Contains("Assimp ") || value.Contains("Imported in-process")));
            Assert.DoesNotContain("Imported in a separate process", inProcessMetadata);
            Assert.Equal(isolatedCounts, ReadCounts(client, import));

            export = client.AddOp("AssimpExport", posX: 320);
            client.ConnectOps(import, "MeshData", export, "MeshData");
            client.SetInput(export, "FolderPath", new JValue(exportFolder));
            client.SetInput(export, "MeshName", new JValue("smoke"));
            client.SetInput(export, "IsolateExport", new JValue(true));

            client.SetInput(export, "ExportMesh", new JValue(true));
            var isolatedPath = WaitForExport(client, export, previousPath: null);
            Assert.Contains("(separate process)", ReadOutput(client, export, "StatusMessage"));
            AssertFile(isolatedPath);

            client.SetInput(export, "IsolateExport", new JValue(false));
            client.SetInput(export, "ExportMesh", new JValue(true));
            var inProcessPath = WaitForExport(client, export, isolatedPath);
            Assert.Contains("(in-process)", ReadOutput(client, export, "StatusMessage"));
            AssertFile(inProcessPath);
            Assert.NotEqual(isolatedPath, inProcessPath);

            var errors = client.GetLogTail(baselineLogSeq, "error");
            Assert.True(errors.Count == 0, "errors during run: " + string.Join(" | ", errors.Select(e => e["message"])));
        }
        finally
        {
            foreach (var child in new[] { export, import })
            {
                if (child == Guid.Empty)
                    continue;

                try
                {
                    client.DeleteOp(child);
                }
                catch (Exception)
                {
                    // Best effort: the fixture tears the editor down anyway
                }
            }

            client.PumpFrames(5);
            try
            {
                if (Directory.Exists(exportFolder))
                    Directory.Delete(exportFolder, recursive: true);
            }
            catch (Exception)
            {
                // The next run skips existing numbered files
            }
        }
    }

    private static string ReadOutput(DebugProtocolClient client, Guid child, string name)
    {
        return client.GetOutputValue(child, name, update: true) ?? string.Empty;
    }

    private static string WaitForOutput(DebugProtocolClient client, Guid child, string name, Func<string?, bool> predicate)
    {
        var deadline = DateTime.UtcNow + Timeout;
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            client.PumpFrames(5);
            last = client.GetOutputValue(child, name, update: true);
            if (predicate(last))
                return last!;
        }

        throw new Xunit.Sdk.XunitException($"Output '{name}' did not reach the expected state; last value: {last ?? "<null>"}");
    }

    private static string WaitForExport(DebugProtocolClient client, Guid export, string? previousPath)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            client.PumpFrames(5);
            var path = client.GetOutputValue(export, "LastFilePath", update: true);
            var success = client.GetOutputValue(export, "Success", update: true);
            if (success == "True" && !string.IsNullOrEmpty(path) && path != previousPath)
                return path;

            var status = client.GetOutputValue(export, "StatusMessage", update: true);
            if (status?.StartsWith("Export failed", StringComparison.Ordinal) == true)
                throw new Xunit.Sdk.XunitException(status);
        }

        throw new Xunit.Sdk.XunitException($"Export did not finish; status: {ReadOutput(client, export, "StatusMessage")}");
    }

    private static Counts ReadCounts(DebugProtocolClient client, Guid import)
    {
        return new Counts(ReadInt(client, import, "VertexCount"),
                          ReadInt(client, import, "FaceCount"),
                          ReadInt(client, import, "MeshCount"));
    }

    private static int ReadInt(DebugProtocolClient client, Guid child, string name)
    {
        var text = client.GetOutputValue(child, name, update: true);
        Assert.True(int.TryParse(text, out var value), $"{name} was '{text ?? "<null>"}'");
        return value;
    }

    private static void AssertFile(string path)
    {
        Assert.False(string.IsNullOrEmpty(path), "export returned no file path");
        Assert.True(File.Exists(path), $"expected exported file at {path}");
        Assert.True(new FileInfo(path).Length > 100, $"exported file at {path} looks empty");
    }

    private readonly record struct Counts(int Vertices, int Faces, int Meshes);
}
