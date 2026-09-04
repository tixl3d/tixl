using Newtonsoft.Json.Linq;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// Verifies the edit -> reload -> compile-errors-or-proceed loop against the
/// hot-reloadable _agentTests project.
/// </summary>
[Collection("Editor")]
public sealed class ReloadTests(EditorFixture editor)
{
    [Fact]
    public void Reload_BrokenSourceReportsErrors_FixedSourceSucceeds()
    {
        var sourcePath = EditorFixture.FindPlaygroundSourceFile();
        var original = File.ReadAllText(sourcePath);
        try
        {
            File.WriteAllText(sourcePath, original + "\nthis is not valid C#\n");
            var broken = editor.Client.Reload(EditorFixture.PlaygroundProject);
            Assert.False(broken.Ok);
            Assert.Equal("COMPILE_FAILED", broken.ErrorCode);
            Assert.Contains("error CS", broken.ErrorDetail);
        }
        finally
        {
            File.WriteAllText(sourcePath, original);
        }

        var fixedResult = editor.Client.Reload(EditorFixture.PlaygroundProject);
        Assert.True(fixedResult.Ok, $"recompile after restore failed: {fixedResult.ErrorDetail}");
        Assert.True(fixedResult.Result!["durationSeconds"]!.Value<double>() > 0);
    }

    [Fact]
    public void Reload_UnknownProject_ReturnsNotFound()
    {
        var response = editor.Client.Reload("DefinitelyNoSuchProject");
        Assert.False(response.Ok);
        Assert.Equal("NOT_FOUND", response.ErrorCode);
    }
}
