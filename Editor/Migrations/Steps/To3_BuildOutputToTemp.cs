#nullable enable
using System.IO;
using T3.Core.Settings;
using T3.Editor.Compilation;
using T3.Editor.Migrations.ProjectFormats;

namespace T3.Editor.Migrations.Steps;

/// <summary>
/// Format V2 -> V3: build output (bin/, obj/) moves under .temp/, leaving the project root with
/// content only. A generated Directory.Build.props roots the MSBuild output paths; the csproj's
/// ClearBuildOutput target switches to the property-based directory; the stale root-level bin/obj
/// folders are removed (tolerantly - locked files just stay behind, inert).
/// </summary>
internal sealed class To3_BuildOutputToTemp : ProjectMigrationStep
{
    public override ProjectFormat TargetFormat => ProjectFormat.V3;
    public override Version ShipsWithEditorVersion => new(4, 3, 0);
    public override string Description => "Move build output under " + FileLocations.TempSubfolder;

    public override void Apply(CsProjectFile csProjectFile)
    {
        var projectFolder = Path.GetFullPath(csProjectFile.Directory);

        WritePropsFile(projectFolder);
        csProjectFile.MigrateCleanBuildTargetToBaseOutputPath();

        // Regenerable; leaving them would only confuse - the editor reads .temp/bin from now on
        TryDeleteDirectory(Path.Combine(projectFolder, "bin"));
        TryDeleteDirectory(Path.Combine(projectFolder, "obj"));
    }

    private static void WritePropsFile(string projectFolder)
    {
        var propsPath = Path.Combine(projectFolder, ProjectXml.BuildPropsFileName);
        if (!File.Exists(propsPath))
        {
            ProjectXml.WriteBuildOutputProps(projectFolder);
            return;
        }

        // A user-authored props file: keep it, but the output paths must still move - without them
        // the editor would look for build output in .temp while MSBuild writes to the root.
        var existing = File.ReadAllText(propsPath);
        if (existing.Contains("BaseOutputPath", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning($"{propsPath} already defines BaseOutputPath - leaving it untouched. "
                        + $"The editor expects build output under {FileLocations.TempSubfolder}/bin.");
            return;
        }

        var closingTag = existing.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
        if (closingTag < 0)
        {
            Log.Warning($"Can't extend {propsPath} - no closing Project tag found.");
            return;
        }

        var propertyGroup = $"""
                               <!-- Added by TiXL: keeps build output out of the project content -->
                               <PropertyGroup>
                                 <BaseOutputPath>{FileLocations.TempSubfolder}/bin/</BaseOutputPath>
                                 <BaseIntermediateOutputPath>{FileLocations.TempSubfolder}/obj/</BaseIntermediateOutputPath>
                                 <MSBuildProjectExtensionsPath>{FileLocations.TempSubfolder}/obj/</MSBuildProjectExtensionsPath>
                               </PropertyGroup>

                             """;
        File.WriteAllText(propsPath, existing[..closingTag] + propertyGroup + existing[closingTag..]);
        Log.Info($"Extended existing {propsPath} with the TiXL build output paths.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't remove stale build output {path}: {e.Message}");
        }
    }
}
