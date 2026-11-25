using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using T3.OperatorCompiler;
using Xunit;
using Xunit.Abstractions;

namespace OperatorCompiler.Tests;

/// <summary>
/// Compiles fixture projects in the shape TiXL writes with the in-process compiler and, where a .NET SDK is installed,
/// checks that the result is equivalent to `dotnet build`: same metadata and IL, same compile options, same generated
/// sources and output files. Tests that need the SDK skip themselves without one.
/// </summary>
public sealed partial class InProcessCompilerTests
{
    public InProcessCompilerTests(ITestOutputHelper output)
    {
        _output = output;

        // Fixture projects reference Core.dll and friends through this variable, as user projects do in the editor
        Environment.SetEnvironmentVariable("T3_ASSEMBLY_PATH", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void CompilesWithShippedSupportFilesOnly()
    {
        using var fixture = new FixtureProject("SampleProject");
        var outputDirectory = fixture.OutputDirectory("Debug");

        var result = InProcessCompiler.Compile(fixture.ProjectPath, "Debug", outputDirectory, _shippedSupport);

        Assert.True(result.Outcome == CompileOutcome.Succeeded, string.Join('\n', result.Errors.Concat(result.NeedsSdkReasons)));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "fixture.SampleProject.dll")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "fixture.SampleProject.pdb")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "fixture.SampleProject.deps.json")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "nested", "nested.txt")), "dependencies/** keeps its folders");
        Assert.False(Directory.Exists(Path.Combine(outputDirectory, "Symbols")), "symbol files are copied in Release only");

        var packageInfo = JsonNode.Parse(File.ReadAllText(Path.Combine(outputDirectory, InProcessCompiler.PackageInfoFileName)))!;
        Assert.Equal("fixture.SampleProject", (string?)packageInfo["RootNamespace"]);
        Assert.Equal("1.2.3", (string?)packageInfo["Version"]);
        Assert.Equal("Lib", (string?)packageInfo["OperatorPackages"]![0]!["Identity"]);

        var dump = MetadataDump.Dump(Path.Combine(outputDirectory, "fixture.SampleProject.dll"));
        Assert.Contains("RegexGenerator_g", dump);
    }

    [Fact]
    public void ReleaseBuildClearsOutputAndCopiesPackageContent()
    {
        using var fixture = new FixtureProject("SampleProject");
        var outputDirectory = fixture.OutputDirectory("Release");
        Directory.CreateDirectory(outputDirectory);
        var staleFile = Path.Combine(outputDirectory, "stale.txt");
        File.WriteAllText(staleFile, "from a previous build");

        var result = InProcessCompiler.Compile(fixture.ProjectPath, "Release", outputDirectory, _shippedSupport);

        Assert.Equal(CompileOutcome.Succeeded, result.Outcome);
        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "Symbols", "math", "ScaledSum.t3")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "SymbolUis", "math", "ScaledSum.t3ui")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "SourceCode", "math", "ScaledSum.cs")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "Assets", "textures", "readme.txt")));
    }

    [Fact]
    public void ProjectsBeyondTheSupportedSubsetNeedTheSdk()
    {
        using var fixture = new FixtureProject("PackageProject");

        var result = InProcessCompiler.Compile(fixture.ProjectPath, "Debug", fixture.OutputDirectory("Debug"), _shippedSupport);

        Assert.Equal(CompileOutcome.NeedsSdk, result.Outcome);
        Assert.Contains("NuGet package Newtonsoft.Json", result.NeedsSdkReasons);
        Assert.Contains("custom build target 'SayHello'", result.NeedsSdkReasons);
        Assert.False(Directory.Exists(fixture.OutputDirectory("Debug")));
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void MatchesDotnetBuild(string configuration)
    {
        if (!TryGetSdkSupport(out var sdkSupport))
            return;

        using var fixture = new FixtureProject("SampleProject");
        var inProcessOutput = Path.Combine(fixture.Directory, "..", "in-process");
        var result = InProcessCompiler.Compile(fixture.ProjectPath, configuration, inProcessOutput, sdkSupport);
        Assert.True(result.Outcome == CompileOutcome.Succeeded, string.Join('\n', result.Errors));

        var project = new EvaluatedProject(fixture.ProjectPath, configuration);
        var generatedSources = new Dictionary<string, string>
                                   {
                                       ["SampleProject.GlobalUsings.g.cs"] = GeneratedSources.GlobalUsings(project),
                                       ["SampleProject.AssemblyInfo.cs"] = GeneratedSources.AssemblyInfo(project)!,
                                       [GeneratedSources.TargetFrameworkAttributeFileName] = GeneratedSources.TargetFrameworkAttribute,
                                   };

        fixture.PinSdkCompilerToEditorRoslyn();
        if (!TryDotnetBuild(fixture, configuration, out _))
            return;

        var sdkOutput = fixture.OutputDirectory(configuration);
        const string assemblyName = "fixture.SampleProject";
        Assert.Equal(MetadataDump.Dump(Path.Combine(sdkOutput, assemblyName + ".dll")),
                     MetadataDump.Dump(Path.Combine(inProcessOutput, assemblyName + ".dll")));

        var sdkOptions = PdbCompilationOptions.Read(Path.Combine(sdkOutput, assemblyName + ".pdb"));
        var inProcessOptions = PdbCompilationOptions.Read(Path.Combine(inProcessOutput, assemblyName + ".pdb"));
        sdkOptions.Remove("compiler-version");
        inProcessOptions.Remove("compiler-version");
        Assert.Equal(sdkOptions.OrderBy(o => o.Key), inProcessOptions.OrderBy(o => o.Key));

        foreach (var (fileName, content) in generatedSources)
            Assert.Equal(File.ReadAllText(Path.Combine(fixture.IntermediateDirectory(configuration), fileName)), content);

        foreach (var jsonFile in (string[])[InProcessCompiler.PackageInfoFileName, assemblyName + ".deps.json"])
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(sdkOutput, jsonFile))),
                                            JsonNode.Parse(File.ReadAllText(Path.Combine(inProcessOutput, jsonFile)))), jsonFile);
        }

        Assert.Equal(RelativeFiles(sdkOutput), RelativeFiles(inProcessOutput));
    }

    [Fact]
    public void ReportsTheSameErrorsAsDotnetBuild()
    {
        if (!TryGetSdkSupport(out var sdkSupport))
            return;

        using var fixture = new FixtureProject("BrokenProject");
        var result = InProcessCompiler.Compile(fixture.ProjectPath, "Debug", Path.Combine(fixture.Directory, "..", "in-process"), sdkSupport);
        Assert.Equal(CompileOutcome.Failed, result.Outcome);

        var (exitCode, sdkLog) = fixture.DotnetBuild("Debug");
        Assert.NotEqual(0, exitCode);

        var expected = ErrorLocations(sdkLog);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, ErrorLocations(string.Join('\n', result.Errors)));
    }

    private bool TryGetSdkSupport(out CompilerSupport support)
    {
        var pack = FixtureProject.FindSdkReferencePack();
        support = pack == null ? _shippedSupport : new CompilerSupport(pack);
        if (pack != null)
            return true;

        Skip("no .NET SDK with a reference pack found to compare with.");
        return false;
    }

    private bool TryDotnetBuild(FixtureProject fixture, string configuration, out string log)
    {
        var (exitCode, output) = fixture.DotnetBuild(configuration);
        log = output;
        if (exitCode == 0)
            return true;

        if (output.Contains("NU1101") || output.Contains("NU1301"))
        {
            Skip("the pinned compiler package could not be restored (offline?).");
            return false;
        }

        Assert.Fail($"dotnet build failed:\n{output}");
        return false;
    }

    /** Comparisons with `dotnet build` skip themselves on machines without an SDK, but CI must not skip them silently. */
    private void Skip(string reason)
    {
        if (Environment.GetEnvironmentVariable("TIXL_REQUIRE_SDK_COMPARISON") == "1")
            Assert.Fail($"Required comparison with dotnet build could not run: {reason}");

        _output.WriteLine($"Skipped: {reason}");
    }

    private static SortedSet<string> RelativeFiles(string directory)
        => new(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                        .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/')), StringComparer.Ordinal);

    /** "file(line,col): error CS0000" of every distinct error; MSBuild repeats errors in its summary. */
    private static SortedSet<string> ErrorLocations(string log)
        => new(ErrorLocation().Matches(log).Select(m => $"{Path.GetFileName(m.Groups[1].Value)}{m.Groups[2].Value}: {m.Groups[3].Value}"),
               StringComparer.Ordinal);

    [GeneratedRegex(@"([^\s:]+\.cs)(\(\d+,\d+\)): error (CS\d+)")]
    private static partial Regex ErrorLocation();

    private static readonly CompilerSupport _shippedSupport = new(Path.Combine(AppContext.BaseDirectory, "CompilerSupport"));
    private readonly ITestOutputHelper _output;
}
