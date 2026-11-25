using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace T3.OperatorCompiler;

/// <summary>
/// Evaluates the subset of MSBuild that TiXL writes into operator project files: properties with configuration and
/// OS conditions, Directory.Build.props, and the items and targets that shape the build output. Anything outside
/// that subset is recorded in <see cref="UnsupportedReasons"/>, so the caller can build with the .NET SDK instead.
/// </summary>
/// <remarks>Like MSBuild, properties are evaluated first in file order, then items and targets with the final properties.</remarks>
internal sealed partial class EvaluatedProject
{
    internal readonly record struct UsingItem(string Namespace, string? Alias, bool IsStatic);

    internal readonly record struct ContentItem(string Include, string Exclude, string? Link, bool AlwaysCopy);

    internal readonly record struct OperatorPackageItem(string Identity, string Version, string ResourcesOnly);

    public EvaluatedProject(string projectPath, string configuration)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        ProjectDirectory = Path.GetDirectoryName(ProjectPath)!;
        Configuration = configuration;

        _properties["Configuration"] = configuration;
        _properties["Platform"] = "AnyCPU";
        _properties["MSBuildProjectDirectory"] = ProjectDirectory;
        _properties["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(ProjectPath);
        _properties["MSBuildProjectFile"] = Path.GetFileName(ProjectPath);
        _properties["MSBuildProjectExtension"] = Path.GetExtension(ProjectPath);
        _properties["MSBuildProjectFullPath"] = ProjectPath;
        _properties["DefaultItemExcludes"] = ";**/*.user;**/*.*proj;**/*.sln;**/*.slnx;**/*.vssscc";

        // Microsoft.NET.Sdk imports the nearest Directory.Build.props before the project body
        var directoryBuildProps = FindFileAbove("Directory.Build.props", ProjectDirectory);
        if (directoryBuildProps != null)
            EvaluatePropertiesOfFile(directoryBuildProps);

        EvaluatePropertiesOfFile(ProjectPath);

        foreach (var (group, fileDirectory) in _itemGroups)
            EvaluateItemGroup(group, fileDirectory);

        foreach (var (target, fileDirectory) in _targets)
            EvaluateTarget(target, fileDirectory);

        CheckProperties();
    }

    public string ProjectPath { get; }
    public string ProjectDirectory { get; }
    public string Configuration { get; }

    /** Why this project needs MSBuild. Empty when the in-process build matches `dotnet build`. */
    public IReadOnlyList<string> UnsupportedReasons => _unsupportedReasons;

    public IReadOnlyList<UsingItem> Usings => _usings;

    /** Compile-only assembly references (Private="false"), as full paths. */
    public IReadOnlyList<string> References => _references;

    public IReadOnlyList<ContentItem> CopiedContent => _copiedContent;
    public IReadOnlyList<OperatorPackageItem> OperatorPackages => _operatorPackages;
    public bool WritesPackageInfo { get; private set; }
    public string? DirectoryToClearBeforeBuild { get; private set; }

    public string Get(string name, string fallback = "")
        => _properties.TryGetValue(name, out var value) && value.Length > 0 ? value : fallback;

    public bool IsTrue(string name) => Get(name).Equals("true", StringComparison.OrdinalIgnoreCase);

    public string AssemblyName => Get("AssemblyName", Get("MSBuildProjectName"));
    public string RootNamespace => Get("RootNamespace", Get("MSBuildProjectName"));
    public string TargetFramework => Get("TargetFramework");
    public string VersionPrefix => Get("VersionPrefix", "1.0.0");

    public string Version
    {
        get
        {
            var suffix = Get("VersionSuffix");
            return Get("Version", suffix.Length > 0 ? $"{VersionPrefix}-{suffix}" : VersionPrefix);
        }
    }

    public bool IsDebugConfiguration => Configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase);
    public bool Optimize => bool.TryParse(Get("Optimize"), out var optimize) ? optimize : !IsDebugConfiguration;
    public string BaseIntermediateOutputPath => Get("BaseIntermediateOutputPath", "obj/");

    /** Where csc writes the assembly before MSBuild copies it to the output folder; generated sources live here too. */
    public string IntermediateDirectory => Path.GetFullPath(Path.Combine(ProjectDirectory, BaseIntermediateOutputPath, Configuration, TargetFramework));

    /// <summary>
    /// The source files of the SDK's default Compile glob (**/*.cs) minus its default excludes (output folders,
    /// hidden folders, DefaultItemExcludes) and the project's Compile Remove patterns, in ordinal order.
    /// </summary>
    public List<string> EnumerateCompileFiles()
    {
        var excludes = new List<string> { "bin/**", "obj/**", "**/.*/**", Get("BaseOutputPath", "bin/") + "**", BaseIntermediateOutputPath + "**" };
        excludes.AddRange(SplitList(Get("DefaultItemExcludes")));
        excludes.AddRange(_compileRemovePatterns);
        var excludePatterns = excludes.Select(GlobToRegex).ToList();

        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ProjectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(ProjectDirectory, file).Replace('\\', '/');
            if (!excludePatterns.Any(p => p.IsMatch(relativePath)))
                files.Add(file);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Resolves an item glob like MSBuild: returns each matching file with its %(RecursiveDir), the part of its folder
    /// below the first wildcard.
    /// </summary>
    public IEnumerable<(string FullPath, string RecursiveDir)> ExpandItem(string include, string exclude)
    {
        var excludePatterns = SplitList(exclude).Select(GlobToRegex).ToList();
        foreach (var rawPattern in SplitList(include))
        {
            var pattern = rawPattern.Replace('\\', '/');
            if (pattern.StartsWith("./", StringComparison.Ordinal))
                pattern = pattern[2..];

            var wildcardIndex = pattern.IndexOfAny(['*', '?']);
            if (wildcardIndex < 0)
            {
                var path = Path.GetFullPath(Path.Combine(ProjectDirectory, pattern));
                if (File.Exists(path))
                    yield return (path, "");
                continue;
            }

            var baseLength = pattern.LastIndexOf('/', wildcardIndex) + 1;
            var baseDirectory = Path.GetFullPath(Path.Combine(ProjectDirectory, pattern[..baseLength]));
            if (!Directory.Exists(baseDirectory))
                continue;

            var regex = GlobToRegex(pattern);
            foreach (var file in Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories))
            {
                var relativeToProject = Path.GetRelativePath(ProjectDirectory, file).Replace('\\', '/');
                if (!regex.IsMatch(relativeToProject) || excludePatterns.Any(p => p.IsMatch(relativeToProject)))
                    continue;

                var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(baseDirectory, file))!.Replace('\\', '/');
                yield return (file, relativeDirectory.Length == 0 ? "" : relativeDirectory + "/");
            }
        }
    }

    private void EvaluatePropertiesOfFile(string path)
    {
        path = Path.GetFullPath(path);
        if (!_evaluatedFiles.Add(path))
            return;

        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (Exception e)
        {
            Unsupported($"{Path.GetFileName(path)} could not be read: {e.Message}");
            return;
        }

        var previousDirectory = Get("MSBuildThisFileDirectory");
        var fileDirectory = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
        _properties["MSBuildThisFileDirectory"] = fileDirectory;

        foreach (var element in document.Root!.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "Import":
                    EvaluateImport(element, fileDirectory);
                    break;
                case "PropertyGroup":
                    if (!IsConditionTrue(element))
                        break;

                    foreach (var property in element.Elements())
                    {
                        if (IsConditionTrue(property))
                            _properties[property.Name.LocalName] = Expand(property.Value.Trim());
                    }

                    break;
                case "ItemGroup":
                    _itemGroups.Add((element, fileDirectory));
                    break;
                case "Target":
                    _targets.Add((element, fileDirectory));
                    break;
                default:
                    Unsupported($"<{element.Name.LocalName}> element");
                    break;
            }
        }

        _properties["MSBuildThisFileDirectory"] = previousDirectory;
    }

    private void EvaluateImport(XElement import, string fileDirectory)
    {
        if (import.Attribute("Sdk") != null)
        {
            Unsupported("SDK import");
            return;
        }

        if (!IsConditionTrue(import))
            return;

        var project = Expand(import.Attribute("Project")?.Value ?? "");
        if (project.Length == 0)
            return;

        var importPath = Path.IsPathRooted(project) ? project : Path.Combine(fileDirectory, project);
        if (File.Exists(importPath))
            EvaluatePropertiesOfFile(importPath);
        else
            Unsupported($"import of {project}, which does not exist");
    }

    private void EvaluateItemGroup(XElement group, string fileDirectory)
    {
        _properties["MSBuildThisFileDirectory"] = fileDirectory;
        if (!IsConditionTrue(group))
            return;

        foreach (var item in group.Elements())
        {
            if (IsConditionTrue(item))
                EvaluateItem(item);
        }
    }

    private void EvaluateItem(XElement item)
    {
        var itemType = item.Name.LocalName;
        var include = Expand(item.Attribute("Include")?.Value ?? "");
        var remove = Expand(item.Attribute("Remove")?.Value ?? "");
        if (item.Attribute("Update") != null)
        {
            Unsupported($"<{itemType} Update=...>");
            return;
        }

        switch (itemType.ToLowerInvariant())
        {
            case "using":
                if (remove.Length > 0)
                {
                    Unsupported("<Using Remove=...>");
                    break;
                }

                foreach (var ns in SplitList(include))
                {
                    var alias = Metadata(item, "Alias");
                    _usings.Add(new UsingItem(ns, string.IsNullOrEmpty(alias) ? null : alias,
                                              string.Equals(Metadata(item, "Static"), "true", StringComparison.OrdinalIgnoreCase)));
                }

                break;
            case "reference":
                foreach (var reference in SplitList(include))
                {
                    var path = Metadata(item, "HintPath") ?? reference;
                    if (!string.Equals(Metadata(item, "Private"), "false", StringComparison.OrdinalIgnoreCase))
                        Unsupported($"reference to {Path.GetFileName(path)} that is copied to the output");
                    else if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        Unsupported($"reference to assembly '{reference}' by name");
                    else if (Metadata(item, "Aliases") != null || Metadata(item, "EmbedInteropTypes") != null)
                        Unsupported($"reference to {Path.GetFileName(path)} with aliases or embedded interop types");
                    else
                        _references.Add(ResolvePath(path));
                }

                break;
            case "content":
            case "none":
                if (remove.Length > 0)
                {
                    Unsupported($"<{itemType} Remove=...>");
                    break;
                }

                var copyMode = Metadata(item, "CopyToOutputDirectory") ?? "";
                if (copyMode.Length == 0 || copyMode.Equals("Never", StringComparison.OrdinalIgnoreCase))
                    break;

                var alwaysCopy = copyMode.Equals("Always", StringComparison.OrdinalIgnoreCase);
                if (!alwaysCopy && !copyMode.Equals("PreserveNewest", StringComparison.OrdinalIgnoreCase))
                {
                    Unsupported($"CopyToOutputDirectory={copyMode}");
                    break;
                }

                // Link metadata keeps its %(...) placeholders: Metadata() would expand them as properties
                var link = item.Attribute("Link")?.Value
                           ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == "Link")?.Value.Trim();
                if (link != null && LinkMetadataReference().Replace(link, "").Contains("%("))
                {
                    Unsupported($"Link metadata '{link}'");
                    break;
                }

                _copiedContent.Add(new ContentItem(include, Expand(item.Attribute("Exclude")?.Value ?? ""),
                                                   link == null ? null : Expand(link), alwaysCopy));
                break;
            case "operators":
                foreach (var identity in SplitList(include))
                    _operatorPackages.Add(new OperatorPackageItem(identity, Metadata(item, "Version") ?? "", Metadata(item, "ResourcesOnly") ?? ""));
                break;
            case "compile":
                if (include.Length > 0)
                    Unsupported("<Compile Include=...>");
                _compileRemovePatterns.AddRange(SplitList(remove));
                break;
            case "embeddedresource":
                if (include.Length > 0)
                    Unsupported("embedded resources");
                break;
            case "folder":
                break;
            case "packagereference":
                Unsupported($"NuGet package {include}");
                break;
            case "projectreference":
                Unsupported($"reference to project {Path.GetFileName(include)}");
                break;
            default:
                Unsupported($"<{itemType}> items");
                break;
        }
    }

    /// <summary>Accepts the two targets TiXL writes into project files; any other target needs MSBuild.</summary>
    private void EvaluateTarget(XElement target, string fileDirectory)
    {
        _properties["MSBuildThisFileDirectory"] = fileDirectory;
        var name = target.Attribute("Name")?.Value ?? "";
        switch (name)
        {
            case "CreatePackageInfo":
                WritesPackageInfo = IsConditionTrue(target);
                break;
            case "ClearBuildOutput":
            {
                if (!IsConditionTrue(target))
                    break;

                var removeDir = target.Elements().FirstOrDefault(e => e.Name.LocalName == "RemoveDir");
                if (removeDir == null || target.Elements().Count() != 1)
                {
                    Unsupported("ClearBuildOutput target with other tasks than RemoveDir");
                    break;
                }

                DirectoryToClearBeforeBuild = ResolvePath(Expand(removeDir.Attribute("Directories")?.Value ?? ""));
                break;
            }
            default:
                Unsupported($"custom build target '{name}'");
                break;
        }
    }

    /** Properties that change the build in ways the in-process compiler does not reproduce. */
    private void CheckProperties()
    {
        if (TargetFramework != SupportedTargetFramework)
            Unsupported($"target framework '{TargetFramework}'");

        foreach (var name in _unsupportedProperties)
        {
            if (Get(name).Length > 0)
                Unsupported($"property {name}");
        }

        var outputType = Get("OutputType", "Library");
        if (!outputType.Equals("Library", StringComparison.OrdinalIgnoreCase))
            Unsupported($"OutputType {outputType}");

        foreach (var name in (string[])["EnableDefaultItems", "EnableDefaultCompileItems", "AppendTargetFrameworkToOutputPath"])
        {
            if (Get(name).Equals("false", StringComparison.OrdinalIgnoreCase))
                Unsupported($"{name}=false");
        }
    }

    private static string? MetadataRaw(XElement item, string name)
        => item.Attribute(name)?.Value
           ?? item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private string? Metadata(XElement item, string name)
    {
        var value = MetadataRaw(item, name);
        return value == null ? null : Expand(value);
    }

    private string ResolvePath(string path)
    {
        path = path.Replace('\\', '/');
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectDirectory, path));
    }

    private void Unsupported(string reason)
    {
        if (!_unsupportedReasons.Contains(reason))
            _unsupportedReasons.Add(reason);
    }

    private static string[] SplitList(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ------------------------------------------------------------------ expansion and conditions

    private string Expand(string value)
    {
        if (!value.Contains("$("))
            return value;

        value = PropertyReference().Replace(value, m => _properties.TryGetValue(m.Groups[1].Value, out var v)
                                                            ? v
                                                            : Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");
        value = MsBuildFunction().Replace(value, m =>
                                                 {
                                                     var args = m.Groups[2].Value.Split(',').Select(a => a.Trim().Trim('\'')).ToArray();
                                                     switch (m.Groups[1].Value)
                                                     {
                                                         case "IsOSPlatform":
                                                             return OperatingSystem.IsOSPlatform(args[0]).ToString();
                                                         case "GetPathOfFileAbove":
                                                             return FindFileAbove(args[0], args.Length > 1 ? args[1] : ProjectDirectory) ?? "";
                                                         case "GetDirectoryNameOfFileAbove":
                                                             return Path.GetDirectoryName(FindFileAbove(args[1], args[0]) ?? "") ?? "";
                                                         default:
                                                             Unsupported($"property function {m.Value}");
                                                             return "";
                                                     }
                                                 });
        if (value.Contains("$("))
            Unsupported($"expression '{value}'");

        return value;
    }

    private bool IsConditionTrue(XElement element)
    {
        var condition = element.Attribute("Condition")?.Value;
        return condition == null || EvaluateCondition(Expand(condition).Trim());
    }

    private bool EvaluateCondition(string condition)
    {
        var ors = Regex.Split(condition, @"\s+or\s+", RegexOptions.IgnoreCase);
        if (ors.Length > 1)
            return ors.Any(c => EvaluateCondition(c.Trim()));

        var ands = Regex.Split(condition, @"\s+and\s+", RegexOptions.IgnoreCase);
        if (ands.Length > 1)
            return ands.All(c => EvaluateCondition(c.Trim()));

        if (condition.StartsWith('(') && condition.EndsWith(')'))
            return EvaluateCondition(condition[1..^1].Trim());

        var comparison = Comparison().Match(condition);
        if (comparison.Success)
        {
            var equal = string.Equals(comparison.Groups[1].Value, comparison.Groups[3].Value, StringComparison.OrdinalIgnoreCase);
            return comparison.Groups[2].Value == "==" ? equal : !equal;
        }

        if (bool.TryParse(condition.Trim('\''), out var literal))
            return literal;

        var exists = ExistsFunction().Match(condition);
        if (exists.Success)
        {
            var path = ResolvePath(exists.Groups[2].Value);
            var found = File.Exists(path) || Directory.Exists(path);
            return exists.Groups[1].Success ? !found : found;
        }

        Unsupported($"condition '{condition}'");
        return false;
    }

    private static string? FindFileAbove(string fileName, string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory)); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /** MSBuild glob to regex, matched against project-relative paths with forward slashes. */
    private static Regex GlobToRegex(string glob)
    {
        glob = glob.Replace('\\', '/');
        if (glob.StartsWith("./", StringComparison.Ordinal))
            glob = glob[2..];

        var pattern = Regex.Escape(glob)
                           .Replace(@"\*\*/", "\u0001")
                           .Replace(@"\*\*", "\u0002")
                           .Replace(@"\*", "[^/]*")
                           .Replace(@"\?", "[^/]")
                           .Replace("\u0001", "(.*/)?")
                           .Replace("\u0002", ".*");
        return new Regex("^" + pattern + "$", RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\$\(([A-Za-z_][A-Za-z0-9_.\-]*)\)")]
    private static partial Regex PropertyReference();

    [GeneratedRegex(@"\$\(\[MSBuild\]::(\w+)\((.*?)\)\)")]
    private static partial Regex MsBuildFunction();

    [GeneratedRegex(@"^'([^']*)'\s*(==|!=)\s*'([^']*)'$")]
    private static partial Regex Comparison();

    [GeneratedRegex(@"^(!)?Exists\('([^']*)'\)$")]
    private static partial Regex ExistsFunction();

    [GeneratedRegex(@"%\((RecursiveDir|Filename|Extension)\)")]
    internal static partial Regex LinkMetadataReference();

    internal const string SupportedTargetFramework = "net10.0";

    private static readonly string[] _unsupportedProperties =
        [
            "TargetFrameworks", "RuntimeIdentifier", "RuntimeIdentifiers", "UseWindowsForms", "UseWPF", "OutputPath",
            "Features", "AdditionalFileItemNames", "PublishAot", "IsAotCompatible",
        ];

    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _evaluatedFiles = new(StringComparer.Ordinal);
    private readonly List<(XElement Group, string FileDirectory)> _itemGroups = [];
    private readonly List<(XElement Target, string FileDirectory)> _targets = [];
    private readonly List<string> _unsupportedReasons = [];
    private readonly List<UsingItem> _usings = [];
    private readonly List<string> _references = [];
    private readonly List<ContentItem> _copiedContent = [];
    private readonly List<OperatorPackageItem> _operatorPackages = [];
    private readonly List<string> _compileRemovePatterns = [];
}
