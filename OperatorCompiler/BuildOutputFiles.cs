using System.Text.Json;
using System.Text.Json.Nodes;

namespace T3.OperatorCompiler;

/** The files MSBuild writes next to the assembly besides the compiler output. */
internal static class BuildOutputFiles
{
    /** The content of the CreatePackageInfo target TiXL writes into project files. */
    public static string PackageInfoJson(EvaluatedProject project)
    {
        var operatorPackages = new JsonArray();
        foreach (var package in project.OperatorPackages)
        {
            operatorPackages.Add(new JsonObject
                                     {
                                         ["Identity"] = package.Identity,
                                         ["Version"] = package.Version,
                                         ["ResourcesOnly"] = package.ResourcesOnly,
                                     });
        }

        var info = new JsonObject
                       {
                           ["HomeGuid"] = project.Get("HomeGuid"),
                           ["PackageId"] = project.Get("PackageId"),
                           ["RootNamespace"] = project.RootNamespace,
                           ["AssemblyFileName"] = project.RootNamespace,
                           ["Version"] = project.VersionPrefix,
                           ["EditorVersion"] = project.Get("EditorVersion"),
                           ["IsEditorOnly"] = project.Get("IsEditorOnly"),
                           ["OperatorPackages"] = operatorPackages,
                       };
        return info.ToJsonString(_indented) + "\n";
    }

    /** The deps.json of a project without package or project references: only its own assembly. */
    public static string DepsJson(EvaluatedProject project)
    {
        var key = $"{project.AssemblyName}/{project.Version}";
        var deps = new JsonObject
                       {
                           ["runtimeTarget"] = new JsonObject { ["name"] = ".NETCoreApp,Version=v10.0", ["signature"] = "" },
                           ["compilationOptions"] = new JsonObject(),
                           ["targets"] = new JsonObject
                                             {
                                                 [".NETCoreApp,Version=v10.0"] = new JsonObject
                                                                                     {
                                                                                         [key] = new JsonObject
                                                                                                     {
                                                                                                         ["runtime"] = new JsonObject
                                                                                                                           { [project.AssemblyName + ".dll"] = new JsonObject() },
                                                                                                     },
                                                                                     },
                                             },
                           ["libraries"] = new JsonObject
                                               {
                                                   [key] = new JsonObject { ["type"] = "project", ["serviceable"] = false, ["sha512"] = "" },
                                               },
                       };
        return deps.ToJsonString(_indented);
    }

    /** Copies Content items with CopyToOutputDirectory to their Link path, like MSBuild's _CopyOutOfDateSourceItemsToOutputDirectory. */
    public static void CopyContent(EvaluatedProject project, string outputDirectory)
    {
        foreach (var item in project.CopiedContent)
        {
            foreach (var (sourcePath, recursiveDir) in project.ExpandItem(item.Include, item.Exclude))
            {
                var relativeTarget = item.Link != null
                                         ? EvaluatedProject.LinkMetadataReference().Replace(item.Link, m => m.Groups[1].Value switch
                                                                                                                {
                                                                                                                    "RecursiveDir" => recursiveDir,
                                                                                                                    "Filename"     => Path.GetFileNameWithoutExtension(sourcePath),
                                                                                                                    _              => Path.GetExtension(sourcePath),
                                                                                                                })
                                         : Path.GetRelativePath(project.ProjectDirectory, sourcePath);

                var targetPath = Path.GetFullPath(Path.Combine(outputDirectory, relativeTarget.Replace('\\', '/')));
                var target = new FileInfo(targetPath);
                if (!item.AlwaysCopy && target.Exists && target.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(sourcePath))
                    continue;

                Directory.CreateDirectory(target.DirectoryName!);
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
        }
    }

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };
}
