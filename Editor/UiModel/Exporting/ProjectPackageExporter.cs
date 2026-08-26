#nullable enable
using System.IO;
using System.IO.Compression;
using System.Text;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using T3.Core.Video;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Editor.UiModel.Exporting;

/// <summary>
/// Exports an editable project as a self-contained, source-only package file (a content-only .nupkg:
/// zip + nuspec manifest). The archive holds the project files in their normal folder layout, so unzipping
/// it into a projects folder yields a working project; external operator packages are recorded as
/// dependency entries in the manifest, never copied in (symbol GUIDs are global — vendored copies would
/// create ambiguous resolution).
/// </summary>
internal static class ProjectPackageExporter
{
    /// <summary>
    /// The subdirectories (relative to the project root) that make up a shareable project; everything
    /// else (bin, obj, .git, .temp backups, exports, stray user files) stays local. The only root-level
    /// files shipped are the csproj(s). Import uses the same list to clean a target directory.
    /// </summary>
    public static readonly string[] IncludedSubdirectories =
        [
            FileLocations.SymbolsSubfolder,
            FileLocations.AssetsSubfolder,
            FileLocations.DependenciesFolder,
            FileLocations.MetaSubFolder,
        ];

    /// <summary>
    /// What a share export would contain: the manifest dependencies, detected cross-project references
    /// (unsupported — abort), and the potential gains of the two opt-in reductions.
    /// </summary>
    public sealed class Analysis
    {
        public required EditableSymbolProject Project { get; init; }
        public required string PackageId { get; init; }
        public required string VersionString { get; init; }

        /// <summary>References into other editable user projects. Sharing those is unsupported; export must abort.</summary>
        public required List<string> CrossProjectReferences { get; init; }

        public required int TotalSymbolCount { get; init; }

        /// <summary>Symbols not reachable from the home symbol — candidates for the opt-in tree-shake.</summary>
        public required HashSet<Guid> UnreachableSymbolIds { get; init; }

        /// <summary>False when the project has no home symbol; reachability can't be derived then.</summary>
        public required bool CanComputeReachability { get; init; }

        /// <summary>Asset files no operator input references — candidates for the opt-in asset exclusion.</summary>
        public required List<string> UnreferencedAssetFiles { get; init; }

        public required long UnreferencedAssetBytes { get; init; }
    }

    public static bool TryAnalyze(EditableSymbolProject project, out Analysis analysis, out string error)
    {
        error = string.Empty;

        var crossProjectReferences = new List<string>();
        CollectDependencies(project, keptSymbolIds: null, dependencies: new Dictionary<string, Version>(), crossProjectReferences);

        var unreachableSymbolIds = new HashSet<Guid>();
        var canComputeReachability = TryCollectUnreachableSymbols(project, unreachableSymbolIds);

        CollectUnreferencedAssets(project, out var unreferencedAssetFiles, out var unreferencedAssetBytes);

        analysis = new Analysis
                       {
                           Project = project,
                           PackageId = project.CsProjectFile.RootNamespace,
                           VersionString = project.CsProjectFile.VersionString,
                           CrossProjectReferences = crossProjectReferences,
                           TotalSymbolCount = project.Symbols.Count,
                           UnreachableSymbolIds = unreachableSymbolIds,
                           CanComputeReachability = canComputeReachability,
                           UnreferencedAssetFiles = unreferencedAssetFiles,
                           UnreferencedAssetBytes = unreferencedAssetBytes,
                       };
        return true;
    }

    public static bool TryExport(Analysis analysis, string targetFolder,
                                 bool stripUnusedSymbols, bool excludeUnreferencedAssets,
                                 out string reason, out string packageFilePath)
    {
        packageFilePath = string.Empty;
        var project = analysis.Project;

        if (analysis.CrossProjectReferences.Count > 0)
        {
            reason = "Can't share a project that uses operators from other user projects:\n"
                     + string.Join("\n", analysis.CrossProjectReferences);
            return false;
        }

        project.SaveModifiedSymbols();

        var keptSymbolIds = stripUnusedSymbols && analysis.CanComputeReachability
                                ? GetKeptSymbolIds(project, analysis.UnreachableSymbolIds)
                                : null;

        // With tree-shaking, only the kept symbols define the dependency block
        var dependencies = new Dictionary<string, Version>();
        var crossRefs = new List<string>();
        CollectDependencies(project, keptSymbolIds, dependencies, crossRefs);

        var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (keptSymbolIds != null)
        {
            CollectStrippedSymbolFiles(project, analysis.UnreachableSymbolIds, excludedFiles);
        }

        if (excludeUnreferencedAssets)
        {
            foreach (var path in analysis.UnreferencedAssetFiles)
            {
                excludedFiles.Add(path);
            }
        }

        var packageFileName = $"{analysis.PackageId}.{analysis.VersionString}.nupkg";
        packageFilePath = Path.Combine(targetFolder, packageFileName);

        try
        {
            Directory.CreateDirectory(targetFolder);

            using var fileStream = File.Create(packageFilePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            var contentFiles = CollectContentFiles(project, excludedFiles);

            WriteTextEntry(archive, $"{analysis.PackageId}.nuspec", BuildNuspec(analysis, dependencies));
            WriteTextEntry(archive, "_rels/.rels", BuildRelationships(analysis.PackageId));
            WriteTextEntry(archive, "[Content_Types].xml", BuildContentTypes(contentFiles));

            var shippedBytes = 0L;
            foreach (var (absolutePath, relativePath) in contentFiles)
            {
                var entryName = relativePath.Replace('\\', '/');
                archive.CreateEntryFromFile(absolutePath, entryName);
                shippedBytes += GetFileLength(absolutePath);
            }

            Log.Info($"Shared project package contains {contentFiles.Count} files ({FormatBytes(shippedBytes)}), "
                     + $"{dependencies.Count} package dependencies.");
        }
        catch (Exception e)
        {
            reason = $"Failed to write package {packageFilePath}: {e.Message}";
            TryDeleteFile(packageFilePath);
            return false;
        }

        reason = $"Exported project package to {packageFilePath}";
        return true;
    }

    #region dependency and reachability analysis
    /// <summary>
    /// Records the external operator packages used by the project's symbols (restricted to
    /// <paramref name="keptSymbolIds"/> when set). References into other user projects are
    /// unsupported and reported separately.
    /// </summary>
    private static void CollectDependencies(EditableSymbolProject project, HashSet<Guid>? keptSymbolIds,
                                            Dictionary<string, Version> dependencies, List<string> crossProjectReferences)
    {
        foreach (var symbol in project.Symbols.Values)
        {
            if (keptSymbolIds != null && !keptSymbolIds.Contains(symbol.Id))
                continue;

            foreach (var child in symbol.Children.Values)
            {
                var childPackage = child.Symbol.SymbolPackage;
                if (childPackage == project)
                    continue;

                var isSharedPackage = childPackage.IsReadOnly || childPackage is EditableSymbolProject { IsBuiltIn: true };
                if (!isSharedPackage)
                {
                    crossProjectReferences.Add($"[{symbol.Name}] uses [{child.Symbol.Name}] from project \"{childPackage.DisplayName}\"");
                    continue;
                }

                var identity = childPackage.RootNamespace;
                var version = childPackage.AssemblyInformation.TryGetReleaseInfo(out var releaseInfo)
                                  ? releaseInfo.Version
                                  : new Version(1, 0, 0);

                if (!dependencies.TryGetValue(identity, out var known) || version > known)
                {
                    dependencies[identity] = version;
                }
            }
        }
    }

    /// <summary>
    /// Marks the project symbols not reachable from the home symbol's static child graph.
    /// Under-approximate by design: symbols referenced only from C# code or temporarily
    /// disconnected are reported as unreachable — that's why tree-shaking is opt-in.
    /// </summary>
    private static bool TryCollectUnreachableSymbols(EditableSymbolProject project, HashSet<Guid> unreachableSymbolIds)
    {
        if (!project.HasHomeSymbol(out _) || !project.Symbols.TryGetValue(project.HomeSymbolId, out var homeSymbol))
            return false;

        var reached = new HashSet<Guid>();
        var stack = new Stack<Symbol>();
        stack.Push(homeSymbol);
        reached.Add(homeSymbol.Id);

        while (stack.Count > 0)
        {
            var symbol = stack.Pop();
            foreach (var child in symbol.Children.Values)
            {
                var childSymbol = child.Symbol;

                // Stop at package boundaries - external packages ship as dependencies, not content
                if (childSymbol.SymbolPackage != project)
                    continue;

                if (reached.Add(childSymbol.Id))
                {
                    stack.Push(childSymbol);
                }
            }
        }

        foreach (var symbolId in project.Symbols.Keys)
        {
            if (!reached.Contains(symbolId))
            {
                unreachableSymbolIds.Add(symbolId);
            }
        }

        return true;
    }

    private static HashSet<Guid> GetKeptSymbolIds(EditableSymbolProject project, HashSet<Guid> unreachableSymbolIds)
    {
        var kept = new HashSet<Guid>();
        foreach (var symbolId in project.Symbols.Keys)
        {
            if (!unreachableSymbolIds.Contains(symbolId))
                kept.Add(symbolId);
        }

        return kept;
    }

    private static void CollectStrippedSymbolFiles(EditableSymbolProject project, HashSet<Guid> strippedSymbolIds, HashSet<string> excludedFiles)
    {
        foreach (var symbolId in strippedSymbolIds)
        {
            if (!project.Symbols.TryGetValue(symbolId, out var symbol))
                continue;

            if (project.TryGetSymbolFilePath(symbol, out var symbolPath))
                excludedFiles.Add(Path.GetFullPath(symbolPath));

            if (project.TryGetSourceCodePath(symbol, out var sourcePath) && sourcePath != null)
                excludedFiles.Add(Path.GetFullPath(sourcePath));

            if (project.TryGetSymbolUiFilePath(symbol, out var uiPath))
                excludedFiles.Add(Path.GetFullPath(uiPath));
        }
    }
    #endregion

    #region asset usage analysis
    /// <summary>
    /// Splits the project's asset files into referenced and unreferenced by scanning all file/directory
    /// inputs of the project's operators. Under-approximate like the symbol reachability — procedural
    /// addresses built in C# are invisible here, so excluding unreferenced assets stays opt-in.
    /// </summary>
    private static void CollectUnreferencedAssets(EditableSymbolProject project, out List<string> unreferencedFiles, out long unreferencedBytes)
    {
        unreferencedFiles = [];
        unreferencedBytes = 0;

        var assetsFolder = project.AssetsFolder;
        if (string.IsNullOrEmpty(assetsFolder) || !Directory.Exists(assetsFolder))
            return;

        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectReferencedAssetFiles(project, referencedFiles);

        foreach (var path in Directory.EnumerateFiles(assetsFolder, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(path);
            if (referencedFiles.Contains(fullPath))
                continue;

            // Generated sidecars are always excluded from packing - don't count them as toggle gain
            if (IsGeneratedSidecarFile(Path.GetFileName(fullPath)))
                continue;

            unreferencedFiles.Add(fullPath);
            unreferencedBytes += GetFileLength(fullPath);
        }
    }

    private static void CollectReferencedAssetFiles(EditableSymbolProject project, HashSet<string> referencedFiles)
    {
        foreach (var symbol in project.Symbols.Values)
        {
            foreach (var child in symbol.Children.Values)
            {
                if (!child.Symbol.TryGetSymbolUi(out var childSymbolUi))
                    continue;

                foreach (var (inputId, input) in child.Inputs)
                {
                    if (!childSymbolUi.InputUis.TryGetValue(inputId, out var inputUi))
                        continue;

                    if (inputUi is not StringInputUi stringInputUi)
                        continue;

                    if (stringInputUi.Usage != StringInputUi.UsageType.FilePath && stringInputUi.Usage != StringInputUi.UsageType.DirectoryPath)
                        continue;

                    if (input.Value is not InputValue<string> stringValue || string.IsNullOrWhiteSpace(stringValue.Value))
                        continue;

                    MarkAddressAsReferenced(project, stringValue.Value, stringInputUi.Usage, referencedFiles);
                }
            }
        }
    }

    private static void MarkAddressAsReferenced(EditableSymbolProject project, string address, StringInputUi.UsageType usage,
                                                HashSet<string> referencedFiles)
    {
        // Resolve through the registry lookup only - TryResolveAddress's fallback search covers
        // just *shared* packages and would miss a non-shared project's own folders.
        if (!AssetRegistry.TryGetAsset(address, out var asset) || !ReferenceEquals(asset.Package, project))
            return;

        if (usage == StringInputUi.UsageType.DirectoryPath)
        {
            if (!asset.IsDirectory || !Directory.Exists(asset.FullPath))
                return;

            foreach (var path in Directory.EnumerateFiles(asset.FullPath, "*", SearchOption.AllDirectories))
            {
                referencedFiles.Add(Path.GetFullPath(path));
            }

            return;
        }

        MarkAssetFileAsReferenced(project, asset.FullPath, referencedFiles);
    }

    private static void MarkAssetFileAsReferenced(EditableSymbolProject project, string absolutePath, HashSet<string> referencedFiles)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        if (!referencedFiles.Add(fullPath))
            return;

        // Bitmap fonts pull in their texture
        if (fullPath.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase))
        {
            var pngPath = Path.ChangeExtension(fullPath, ".png");
            if (File.Exists(pngPath))
                referencedFiles.Add(pngPath);
        }

        // Shaders pull in their includes (recursively, via the Add-guard above)
        if (fullPath.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
        {
            MarkShaderIncludesAsReferenced(project, fullPath, referencedFiles);
        }
    }

    private static void MarkShaderIncludesAsReferenced(EditableSymbolProject project, string shaderPath, HashSet<string> referencedFiles)
    {
        string shaderText;
        try
        {
            shaderText = File.ReadAllText(shaderPath);
        }
        catch (Exception e)
        {
            Log.Warning($"Can't scan shader includes of {shaderPath}: {e.Message}");
            return;
        }

        var shaderDirectory = Path.GetDirectoryName(shaderPath);
        foreach (var includePath in ShaderCompiler.GetIncludesFrom(shaderText))
        {
            if (shaderDirectory != null)
            {
                var localCandidate = Path.GetFullPath(Path.Combine(shaderDirectory, includePath));
                if (File.Exists(localCandidate))
                {
                    MarkAssetFileAsReferenced(project, localCandidate, referencedFiles);
                    continue;
                }
            }

            if (ShaderCompiler.TryResolveSharedIncludeAsset(includePath, out var includeAsset)
                && ReferenceEquals(includeAsset.Package, project))
            {
                MarkAssetFileAsReferenced(project, includeAsset.FullPath, referencedFiles);
            }
        }
    }
    #endregion

    #region package writing
    private static List<(string AbsolutePath, string RelativePath)> CollectContentFiles(EditableSymbolProject project, HashSet<string> excludedFiles)
    {
        var projectFolder = Path.GetFullPath(project.Folder);
        var files = new List<(string, string)>();

        // Root level: only the project file(s)
        foreach (var path in Directory.EnumerateFiles(projectFolder, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(path);
            files.Add((fullPath, Path.GetRelativePath(projectFolder, fullPath)));
        }

        foreach (var subdirectory in IncludedSubdirectories)
        {
            var subdirectoryPath = Path.Combine(projectFolder, subdirectory);
            if (!Directory.Exists(subdirectoryPath))
                continue;

            foreach (var path in Directory.EnumerateFiles(subdirectoryPath, "*", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(path);
                var fileName = Path.GetFileName(fullPath);

                if (FileLocations.IgnoredFiles.Contains(fileName))
                    continue;

                if (IsGeneratedSidecarFile(fileName))
                    continue;

                if (excludedFiles.Contains(fullPath))
                    continue;

                files.Add((fullPath, Path.GetRelativePath(projectFolder, fullPath)));
            }
        }

        return files;
    }

    /// <summary>
    /// Generated sibling files the editor recreates on demand: video preview proxies and
    /// legacy audio waveform caches (current waveform caches live in the user's temp folder).
    /// </summary>
    private static bool IsGeneratedSidecarFile(string fileName)
    {
        return fileName.EndsWith(VideoPlayback.ProxySuffix, StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".waveform.png", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNuspec(Analysis analysis, Dictionary<string, Version> dependencies)
    {
        var author = analysis.PackageId.Split('.')[0];

        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.AppendLine("""<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">""");
        sb.AppendLine("  <metadata>");
        sb.AppendLine($"    <id>{XmlEscape(analysis.PackageId)}</id>");
        sb.AppendLine($"    <version>{XmlEscape(analysis.VersionString)}</version>");
        sb.AppendLine($"    <authors>{XmlEscape(author)}</authors>");
        sb.AppendLine($"    <description>TiXL project package \"{XmlEscape(analysis.PackageId)}\". "
                      + "Source-only: the TiXL editor compiles it at startup like any local project.</description>");
        sb.AppendLine("    <tags>tixl project</tags>");
        sb.AppendLine("    <packageTypes>");
        sb.AppendLine("""      <packageType name="TixlProject" />""");
        sb.AppendLine("    </packageTypes>");

        if (dependencies.Count > 0)
        {
            var sortedIdentities = new List<string>(dependencies.Keys);
            sortedIdentities.Sort(StringComparer.OrdinalIgnoreCase);

            sb.AppendLine("    <dependencies>");
            sb.AppendLine("      <group>");
            foreach (var identity in sortedIdentities)
            {
                sb.AppendLine($"""        <dependency id="{XmlEscape(identity)}" version="{dependencies[identity]}" />""");
            }

            sb.AppendLine("      </group>");
            sb.AppendLine("    </dependencies>");
        }

        sb.AppendLine("  </metadata>");
        sb.AppendLine("</package>");
        return sb.ToString();
    }

    private static string BuildRelationships(string packageId)
    {
        return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{XmlEscape(packageId)}.nuspec" Id="Rmanifest" />
                </Relationships>
                """;
    }

    private static string BuildContentTypes(List<(string AbsolutePath, string RelativePath)> contentFiles)
    {
        var extensions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "rels", "nuspec" };
        var extensionlessFiles = new List<string>();

        foreach (var (_, relativePath) in contentFiles)
        {
            var extension = Path.GetExtension(relativePath);
            if (extension.Length > 1)
            {
                extensions.Add(extension[1..]);
            }
            else
            {
                extensionlessFiles.Add(relativePath.Replace('\\', '/'));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.AppendLine("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        foreach (var extension in extensions)
        {
            var contentType = extension.Equals("rels", StringComparison.OrdinalIgnoreCase)
                                  ? "application/vnd.openxmlformats-package.relationships+xml"
                                  : "application/octet";
            sb.AppendLine($"""  <Default Extension="{XmlEscape(extension)}" ContentType="{contentType}" />""");
        }

        foreach (var partName in extensionlessFiles)
        {
            sb.AppendLine($"""  <Override PartName="/{XmlEscape(partName)}" ContentType="application/octet" />""");
        }

        sb.AppendLine("</Types>");
        return sb.ToString();
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string XmlEscape(string value)
    {
        return value.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
    }
    #endregion

    private static long GetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't clean up incomplete package file {path}: {e.Message}");
        }
    }

    public static string FormatBytes(long bytes)
    {
        return bytes >= 1024 * 1024
                   ? $"{bytes / (1024.0 * 1024.0):0.0} MB"
                   : $"{bytes / 1024.0:0.0} KB";
    }
}
