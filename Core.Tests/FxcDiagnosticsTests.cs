using System.Collections.Generic;
using T3.Core.Resource.ShaderCompiling;
using Xunit;

namespace Core.Tests;

/// <summary>The inputs follow the D3D compiler's "file(line,columns): severity code: text" output.</summary>
public class FxcDiagnosticsTests
{
    [Fact]
    public void TheMessageHasOnlyTheLineNumberNotTheColumns()
    {
        var parsed = new List<ShaderCompileFailures.Diagnostic>();

        var message = FxcDiagnostics.Describe("Blur.hlsl(40,5-12): error X3004: undeclared identifier 'q'", "Blur.hlsl", parsed);

        Assert.Equal("Line 40: undeclared identifier 'q'\n\nBlur.hlsl", message);
        Assert.Equal([new ShaderCompileFailures.Diagnostic(true, "", 40, 5, "undeclared identifier 'q'")], parsed);
    }

    [Fact]
    public void AnErrorInAnIncludeKeepsTheIncludeAndIsPreferredOverAnEarlierWarning()
    {
        const string output = """
                              Blur.hlsl(3,1): warning X3206: implicit truncation of vector type
                              C:\TiXL\Lib\shared\point.hlsl(12,3-33,4): error X3000: syntax error: unexpected token '}'
                              """;
        var parsed = new List<ShaderCompileFailures.Diagnostic>();

        var message = FxcDiagnostics.Describe(output, "Blur.hlsl", parsed);

        Assert.Equal("Line 12: syntax error: unexpected token '}'\n\nC:\\TiXL\\Lib\\shared\\point.hlsl", message);
        Assert.Equal([
                         new ShaderCompileFailures.Diagnostic(false, "", 3, 1, "implicit truncation of vector type"),
                         new ShaderCompileFailures.Diagnostic(true, @"C:\TiXL\Lib\shared\point.hlsl", 12, 3, "syntax error: unexpected token '}'"),
                     ], parsed);
    }

    [Fact]
    public void AnErrorWithoutLocationIsKeptWithLineZero()
    {
        var parsed = new List<ShaderCompileFailures.Diagnostic>();

        var message = FxcDiagnostics.Describe("error X3501: 'main': entrypoint not found", "Blur.hlsl", parsed);

        Assert.Equal("'main': entrypoint not found", message);
        Assert.Equal([new ShaderCompileFailures.Diagnostic(true, "", 0, 0, "'main': entrypoint not found")], parsed);
    }

    [Fact]
    public void OutputThatIsNotADiagnosticIsPassedThrough()
    {
        Assert.Equal("Access denied", FxcDiagnostics.Describe("Access denied", "Blur.hlsl", []));
    }
}
