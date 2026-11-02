using T3.Core.Resource.ShaderCompiling;
using Xunit;

namespace Core.Tests;

/// <summary>The inputs are slangc 2026.18's actual output for each kind of error.</summary>
public class SlangDiagnosticsTests
{
    private const string TempSource = "/tmp/tixl-slang/Blur.main.1.hlsl";

    [Fact]
    public void TheMessageNamesTheIdentifierLineAndShader()
    {
        const string diagnostics = """
                                   error[E30015]: undefined identifier
                                     --> /tmp/tixl-slang/Blur.main.1.hlsl:78:31
                                      |
                                   78 | float3 d = rotate_vector(FORWARD, q);
                                      |            ^^^^^^^^^^^^^ undefined identifier 'rotate_vector'.
                                   ---'
                                   """;

        var message = SlangDiagnostics.DescribeFirstError(diagnostics, TempSource, "Blur.hlsl");

        Assert.StartsWith("Line 78: undefined identifier 'rotate_vector'.\n\nBlur.hlsl", message);
        Assert.Contains("qRotateVec3", message);
    }

    [Fact]
    public void WarningsBeforeTheErrorAreSkippedEvenIfTheirSourceLineMentionsError()
    {
        const string diagnostics = """
                                   warning[E30081]: implicit conversion not recommended
                                     --> /tmp/tixl-slang/Blur.main.1.hlsl:3:1
                                      |
                                    3 | int error = 1.5;
                                      | ^ implicit conversion
                                   ---'

                                   error[E30019]: type mismatch in expression
                                     --> /tmp/tixl-slang/Blur.main.1.hlsl:9:5
                                      |
                                    9 | s.a=q;
                                      |     ^ expected an expression of type 'vector<float,3>', got 'vector<float,4>'
                                   ---'
                                   """;

        var message = SlangDiagnostics.DescribeFirstError(diagnostics, TempSource, "Blur.hlsl");

        Assert.StartsWith("Line 9: expected an expression of type", message);
        Assert.Contains("Vector sizes must match", message);
    }

    [Fact]
    public void AnErrorInAnIncludeNamesTheInclude()
    {
        const string diagnostics = """
                                   error[E30015]: undefined identifier
                                     --> /home/me/Assets/shared/my-functions.hlsl:4:1
                                      |
                                    4 | foo();
                                      | ^^^ undefined identifier 'foo'.
                                   ---'
                                   """;

        var message = SlangDiagnostics.DescribeFirstError(diagnostics, TempSource, "Blur.hlsl");

        Assert.Equal("Line 4: undefined identifier 'foo'.\n\nmy-functions.hlsl", message);
    }

    [Theory]
    [InlineData("ambiguous reference to 'SLANG_ParameterGroup_Params'", "share a name")]
    [InlineData("left of '=' is not an l-value.", "local variable")]
    [InlineData("no overload for '*=' applicable to arguments of type (vector<float,3>, vector<float,4>)", "Vector sizes must match")]
    [InlineData("undefined identifier 'LegacyPoint'.", "FX1")]
    [InlineData("undefined identifier 'sampler2D'.", "Texture2D")]
    public void KnownCausesGetAHint(string text, string expectedHint)
    {
        var diagnostics = $"error[E1]: x\n --> /tmp/a.hlsl:1:1\n  |\n1 | code\n  | ^ {text}\n---'";

        Assert.Contains(expectedHint, SlangDiagnostics.DescribeFirstError(diagnostics, TempSource, "Blur.hlsl"));
    }

    [Fact]
    public void EqualVectorSizesGetNoHint()
    {
        var diagnostics = "error[E1]: x\n --> /tmp/a.hlsl:1:1\n  |\n1 | code\n  | ^ no overload for 'foo' applicable to arguments of type (vector<float,3>, vector<float,3>)\n---'";

        Assert.DoesNotContain("Vector sizes", SlangDiagnostics.DescribeFirstError(diagnostics, TempSource, "Blur.hlsl"));
    }

    [Fact]
    public void OutputThatIsNotADiagnosticIsPassedThrough()
    {
        const string timeout = "slangc did not finish within 60 seconds.";

        Assert.Equal(timeout, SlangDiagnostics.DescribeFirstError(timeout, TempSource, "Blur.hlsl"));
    }
}
