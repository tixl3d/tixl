using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace T3.OperatorCompiler;

public enum CompileOutcome
{
    Succeeded,
    Failed,

    /** The project uses MSBuild features this compiler does not reproduce; build it with the .NET SDK. */
    NeedsSdk,
}

/// <param name="Errors">Compiler errors in csc's format: <c>path(line,col): error CS0000: message</c>.</param>
/// <param name="NeedsSdkReasons">Why the project has to be built with the .NET SDK, for <see cref="CompileOutcome.NeedsSdk"/>.</param>
public sealed record CompileResult(CompileOutcome Outcome, IReadOnlyList<string> Errors, int WarningCount, IReadOnlyList<string> NeedsSdkReasons, long ElapsedMs);

/// <summary>
/// Builds a TiXL operator project with the Roslyn compiler inside the calling process, without the .NET SDK. For the
/// project files TiXL writes, the output matches `dotnet build`: same assembly metadata and IL, same compile options
/// recorded in the PDB, same generated sources, OperatorPackage.json, deps.json and copied content.
/// </summary>
public static class InProcessCompiler
{
    /// <param name="configuration">"Debug" or "Release", as passed to `dotnet build --configuration`.</param>
    /// <param name="outputDirectory">Where `dotnet build` would put the assembly (bin/&lt;configuration&gt;/&lt;tfm&gt;).</param>
    public static CompileResult Compile(string projectPath, string configuration, string outputDirectory, CompilerSupport support)
    {
        var stopwatch = Stopwatch.StartNew();
        var project = new EvaluatedProject(projectPath, configuration);
        if (project.UnsupportedReasons.Count > 0)
            return new CompileResult(CompileOutcome.NeedsSdk, [], 0, project.UnsupportedReasons, stopwatch.ElapsedMilliseconds);

        if (!support.IsAvailable)
            return new CompileResult(CompileOutcome.NeedsSdk, [], 0, [$"the compiler support files are missing in {support.ReferencePackDirectory}"],
                                     stopwatch.ElapsedMilliseconds);

        if (project.DirectoryToClearBeforeBuild != null && Directory.Exists(project.DirectoryToClearBeforeBuild))
            Directory.Delete(project.DirectoryToClearBeforeBuild, recursive: true);

        var compilation = CreateCompilation(project, support, out var parseOptions, out var missingReferences);
        compilation = RunGenerators(compilation, project, support, parseOptions);

        var assemblyPath = Path.Combine(outputDirectory, project.AssemblyName + ".dll");
        var debugType = project.Get("DebugType", "portable").ToLowerInvariant();
        var emitOptions = new EmitOptions(debugInformationFormat: debugType == "embedded"
                                                                      ? DebugInformationFormat.Embedded
                                                                      : DebugInformationFormat.PortablePdb,
                                          pdbFilePath: Path.Combine(project.IntermediateDirectory, project.AssemblyName + ".pdb"));
        var writesPdb = debugType is not ("embedded" or "none");

        using var peStream = new MemoryStream();
        using var pdbStream = writesPdb ? new MemoryStream() : null;
        var emitResult = compilation.Emit(peStream, pdbStream, options: emitOptions);

        var errors = new List<string>(missingReferences.Count);
        var warningCount = 0;
        foreach (var diagnostic in emitResult.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                errors.Add(diagnostic.ToString());
            else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                warningCount++;
        }

        if (!emitResult.Success)
        {
            // MSBuild only warns about a missing reference (MSB3245); name it next to the errors it causes
            foreach (var missing in missingReferences)
                errors.Add($"{project.ProjectPath}: warning MSB3245: Could not resolve this reference: {missing}");
            return new CompileResult(CompileOutcome.Failed, errors, warningCount, [], stopwatch.ElapsedMilliseconds);
        }

        Directory.CreateDirectory(outputDirectory);
        WriteAtomically(assemblyPath, peStream);
        if (pdbStream != null)
            WriteAtomically(Path.ChangeExtension(assemblyPath, ".pdb"), pdbStream);

        File.WriteAllText(Path.Combine(outputDirectory, project.AssemblyName + ".deps.json"), BuildOutputFiles.DepsJson(project), _utf8);
        if (project.WritesPackageInfo)
            File.WriteAllText(Path.Combine(outputDirectory, PackageInfoFileName), BuildOutputFiles.PackageInfoJson(project), _utf8WithBom);

        BuildOutputFiles.CopyContent(project, outputDirectory);
        return new CompileResult(CompileOutcome.Succeeded, [], warningCount, [], stopwatch.ElapsedMilliseconds);
    }

    /** Written by the CreatePackageInfo target of TiXL project files; read by the editor's package loader. */
    public const string PackageInfoFileName = "OperatorPackage.json";

    private static Compilation CreateCompilation(EvaluatedProject project, CompilerSupport support, out CSharpParseOptions parseOptions,
                                                 out List<string> missingReferences)
    {
        if (!LanguageVersionFacts.TryParse(project.Get("LangVersion", "latest"), out var languageVersion))
            languageVersion = LanguageVersion.Latest;

        parseOptions = new CSharpParseOptions(languageVersion, DocumentationMode.Parse, SourceCodeKind.Regular,
                                              GeneratedSources.PreprocessorSymbols(project));

        var sourceFiles = project.EnumerateCompileFiles();
        var trees = new SyntaxTree[sourceFiles.Count];
        var options = parseOptions;
        Parallel.For(0, sourceFiles.Count, index =>
                                           {
                                               using var stream = File.OpenRead(sourceFiles[index]);
                                               var text = SourceText.From(stream, Encoding.UTF8, SourceHashAlgorithm.Sha256, canBeEmbedded: true);
                                               trees[index] = CSharpSyntaxTree.ParseText(text, options, sourceFiles[index]);
                                           });

        var allTrees = new List<SyntaxTree>(trees);
        var generatedDirectory = project.IntermediateDirectory;
        var projectName = project.Get("MSBuildProjectName");
        allTrees.Add(ParseGenerated(GeneratedSources.GlobalUsings(project), Path.Combine(generatedDirectory, projectName + ".GlobalUsings.g.cs"), parseOptions));
        var assemblyInfo = GeneratedSources.AssemblyInfo(project);
        if (assemblyInfo != null)
            allTrees.Add(ParseGenerated(assemblyInfo, Path.Combine(generatedDirectory, projectName + ".AssemblyInfo.cs"), parseOptions));
        allTrees.Add(ParseGenerated(GeneratedSources.TargetFrameworkAttribute,
                                    Path.Combine(generatedDirectory, GeneratedSources.TargetFrameworkAttributeFileName), parseOptions));

        var references = new List<MetadataReference>(support.FrameworkReferences);
        missingReferences = [];
        foreach (var path in project.References)
        {
            if (File.Exists(path))
                references.Add(support.GetReference(path));
            else
                missingReferences.Add(path);
        }

        return CSharpCompilation.Create(project.AssemblyName, allTrees, references, CompilationOptions(project));
    }

    private static CSharpCompilationOptions CompilationOptions(EvaluatedProject project)
    {
        var specificOptions = new Dictionary<string, ReportDiagnostic>();
        foreach (var id in DiagnosticIds(project.Get("WarningsAsErrors")))
            specificOptions[id] = ReportDiagnostic.Error;
        foreach (var id in DiagnosticIds(project.Get("NoWarn")))
            specificOptions[id] = ReportDiagnostic.Suppress;

        var nullable = project.Get("Nullable").ToLowerInvariant() switch
                           {
                               "enable"      => NullableContextOptions.Enable,
                               "warnings"    => NullableContextOptions.Warnings,
                               "annotations" => NullableContextOptions.Annotations,
                               _             => NullableContextOptions.Disable,
                           };

        var platform = project.Get("PlatformTarget", "AnyCPU").ToLowerInvariant() switch
                           {
                               "x64"   => Platform.X64,
                               "x86"   => Platform.X86,
                               "arm64" => Platform.Arm64,
                               _       => Platform.AnyCpu,
                           };

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                     .WithModuleName(project.AssemblyName + ".dll")
                     .WithOptimizationLevel(project.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug)
                     .WithAllowUnsafe(project.IsTrue("AllowUnsafeBlocks"))
                     .WithOverflowChecks(project.IsTrue("CheckForOverflowUnderflow"))
                     .WithNullableContextOptions(nullable)
                     .WithDeterministic(true)
                     .WithPlatform(platform)
                     .WithWarningLevel(int.TryParse(project.Get("WarningLevel"), out var warningLevel) ? warningLevel : SdkWarningLevel)
                     .WithGeneralDiagnosticOption(project.IsTrue("TreatWarningsAsErrors") ? ReportDiagnostic.Error : ReportDiagnostic.Default)
                     .WithSpecificDiagnosticOptions(specificOptions)
                     .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);

        return project.Optimize ? options : WithDebugPlusMode(options);
    }

    /// <summary>
    /// csc's /debug+ without /optimize sets Roslyn's internal debug-plus mode (keeps more locals alive for the
    /// debugger). The public API has no setter; without it only debugging fidelity differs, so a missing method is fine.
    /// </summary>
    private static CSharpCompilationOptions WithDebugPlusMode(CSharpCompilationOptions options)
    {
        var method = typeof(CSharpCompilationOptions).GetMethod("WithDebugPlusMode", BindingFlags.Instance | BindingFlags.NonPublic);
        return method?.Invoke(options, [true]) as CSharpCompilationOptions ?? options;
    }

    private static IEnumerable<string> DiagnosticIds(string list)
    {
        foreach (var id in list.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return char.IsDigit(id[0]) ? "CS" + id.PadLeft(4, '0') : id;
    }

    private static Compilation RunGenerators(Compilation compilation, EvaluatedProject project, CompilerSupport support, CSharpParseOptions parseOptions)
    {
        var generators = support.Generators;
        if (generators.IsEmpty)
            return compilation;

        var globalOptions = new Dictionary<string, string>
                                {
                                    ["build_property.TargetFramework"] = project.TargetFramework,
                                    ["build_property.TargetFrameworkIdentifier"] = ".NETCoreApp",
                                    ["build_property.TargetFrameworkVersion"] = "v10.0",
                                    ["build_property.RootNamespace"] = project.RootNamespace,
                                    ["build_property.ProjectDir"] = project.ProjectDirectory + Path.DirectorySeparatorChar,
                                };

        // csc places generated trees under its output folder; their paths feed the names of file-local types
        var driver = CSharpGeneratorDriver.Create(generators, additionalTexts: [], parseOptions, new OptionsProvider(globalOptions),
                                                  new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: false,
                                                                             baseDirectory: project.IntermediateDirectory));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return updated;
    }

    private static SyntaxTree ParseGenerated(string source, string path, CSharpParseOptions parseOptions)
        => CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8, SourceHashAlgorithm.Sha256), parseOptions, path);

    /** The running editor may load the previous assembly at any time, so it must never see a half-written file. */
    private static void WriteAtomically(string path, MemoryStream content)
    {
        var temporaryPath = path + ".tmp";
        using (var file = File.Create(temporaryPath))
            content.WriteTo(file);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed class OptionsProvider(Dictionary<string, string> globalOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globalOptions);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;

        private static readonly Options _empty = new([]);
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    /** The SDK sets WarningLevel to the target framework's major version, enabling that release's warning waves. */
    private const int SdkWarningLevel = 10;

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding _utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
}
