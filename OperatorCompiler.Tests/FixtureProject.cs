using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OperatorCompiler.Tests;

/// <summary>A copy of a fixture project in a temporary folder, plus helpers to build it with the .NET SDK.</summary>
internal sealed class FixtureProject : IDisposable
{
    public FixtureProject(string fixtureName)
    {
        Directory = Path.Combine(Path.GetTempPath(), "tixl-operator-compiler-tests", Guid.NewGuid().ToString("N")[..8], fixtureName);
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName), Directory);
        ProjectPath = Path.Combine(Directory, fixtureName + ".csproj");
    }

    public string Directory { get; }
    public string ProjectPath { get; }

    public string OutputDirectory(string configuration) => Path.Combine(Directory, ".temp", "bin", configuration, "net10.0");
    public string IntermediateDirectory(string configuration) => Path.Combine(Directory, ".temp", "obj", configuration, "net10.0");

    /** Makes `dotnet build` compile with the Roslyn version the in-process compiler uses, so codegen can't differ by version. */
    public void PinSdkCompilerToEditorRoslyn()
    {
        var propsPath = Path.Combine(Directory, "Directory.Build.props");
        var props = File.ReadAllText(propsPath);
        File.WriteAllText(propsPath, props.Replace("</Project>",
                                                   $"""
                                                      <ItemGroup>
                                                        <PackageReference Include="Microsoft.Net.Compilers.Toolset" Version="{RoslynVersion}" PrivateAssets="all" />
                                                      </ItemGroup>
                                                    </Project>
                                                    """));
    }

    public (int ExitCode, string Output) DotnetBuild(string configuration)
        => Dotnet($"build \"{ProjectPath}\" -c {configuration} -nologo -p:NuGetAudit=false", Directory);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Path.GetDirectoryName(Directory)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /** The reference pack of the installed SDK, so a comparison with `dotnet build` uses the same assemblies and generators. */
    public static string? FindSdkReferencePack()
    {
        var (exitCode, sdkVersion) = Dotnet("--version", AppContext.BaseDirectory);
        if (exitCode != 0)
            return null;

        sdkVersion = sdkVersion.Trim();
        var (_, sdkList) = Dotnet("--list-sdks", AppContext.BaseDirectory);
        var sdkLine = sdkList.Split('\n').FirstOrDefault(l => l.StartsWith(sdkVersion + " ", StringComparison.Ordinal));
        var sdkRoot = sdkLine == null ? null : Regex.Match(sdkLine, @"\[(.*)\]").Groups[1].Value;
        if (string.IsNullOrEmpty(sdkRoot))
            return null;

        var bundledVersions = Path.Combine(sdkRoot, sdkVersion, "Microsoft.NETCoreSdk.BundledVersions.props");
        if (!File.Exists(bundledVersions))
            return null;

        var runtimeVersion = Regex.Match(File.ReadAllText(bundledVersions), "<BundledNETCoreAppPackageVersion>([^<]+)<").Groups[1].Value;
        var pack = Path.Combine(Path.GetDirectoryName(sdkRoot)!, "packs", "Microsoft.NETCore.App.Ref", runtimeVersion);
        return System.IO.Directory.Exists(pack) ? pack : null;
    }

    public static bool IsSdkInstalled => Dotnet("--version", AppContext.BaseDirectory).ExitCode == 0;

    private static (int ExitCode, string Output) Dotnet(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                WorkingDirectory = workingDirectory,
                            };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        try
        {
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit(TimeSpan.FromMinutes(5));
            return (process.ExitCode, output.Result + error.Result);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, e.Message);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /** Microsoft.CodeAnalysis.CSharp version of OperatorCompiler.csproj and Editor.csproj. */
    private const string RoslynVersion = "5.6.0";
}
