#nullable enable
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Construction;
using T3.Core.Compilation;
using T3.Core.Model;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using T3.Editor.UiModel;

namespace T3.Editor.Compilation;

/// <summary>
/// Hosts the logic for creating and modifying a csproj file for a T3 operator package
/// </summary>
internal static partial class ProjectXml
{
    public static ProjectRootElement CreateNewProjectRootElement(string projectNamespace, Guid homeGuid, Guid packageId)
    {
        var rootElement = ProjectRootElement.Create();
        rootElement.Sdk = "Microsoft.NET.Sdk";
        rootElement.AddDefaultPropertyGroup(projectNamespace, homeGuid, packageId);
        rootElement.AddDefaultUsings();
        rootElement.AddDefaultReferenceGroup();
        rootElement.AddDefaultContent();
        rootElement.AddOpPackageItemGroup();
        rootElement.AddPackageInfoTarget();
        rootElement.AddCleanBuildTarget();
        return rootElement;
    }

    [return: NotNullIfNotNull(nameof(defaultValue))]
    public static string? GetOrAddProperty(this ProjectRootElement project, PropertyType propertyType, string? defaultValue = null)
    {
        var propertyName = propertyType.GetItemName();

        ProjectPropertyElement? property = null;
        foreach(var prop in project.Properties)
        {
            if (prop.Name != propertyName)
                continue;
            
            if(property != null)
            {
                throw new Exception($"Multiple properties with the same name: {propertyName}");
            }
            
            property = prop;
        }

        if (property == null && (defaultValue != null || _defaultProperties.TryGetValue(propertyType, out defaultValue)))
        {
            property = project.SetOrAddProperty(propertyType, defaultValue);
        }
            
        return property?.Value;
    }

    public static ProjectPropertyElement SetOrAddProperty(this ProjectRootElement project, PropertyType propertyType, string value)
    {
        var propertyName = propertyType.GetItemName();
        var count = 0;
        ProjectPropertyElement? property = null;

        foreach (var prop in project.Properties)
        {
            if (prop.Name == propertyName)
            {
                property = prop;
                ++count;
            }
        }
        
        if(property == null)
            return project.AddProperty(propertyName, value);

        if (count > 1)
        {
            Log.Error($"Multiple properties with the same name '{propertyName}' in the project file '{project.FullPath}'");
        }
        
        property.Value = value;
        return property;
    }

    private static void AddDefaultUsings(this ProjectRootElement project)
    {
        var itemGroup = project.AddItemGroup();
        foreach (var use in _defaultUsingStatements)
        {
            var item = itemGroup.AddItem("Using", use.Name);
            if (use.Static)
                item.AddMetadata("Static", "True");

            if (use.Alias != null)
            {
                item.AddMetadata("Alias", use.Alias);
            }
        }
    }

    private static void AddDefaultPropertyGroup(this ProjectRootElement project, string projectNamespace, Guid homeGuid, Guid packageId)
    {
        var propertyGroup = project.AddPropertyGroup();
        foreach (var defaultProperty in _defaultProperties)
        {
            var propertyName = defaultProperty.Key.GetItemName();
            if (defaultProperty.Key is PropertyType.HomeGuid or PropertyType.RootNamespace)
            {
                Log.Warning($"Cannot set {propertyName} here - remove it from defaults\n" + Environment.StackTrace);
                continue;
            }

            propertyGroup.AddProperty(propertyName, defaultProperty.Value);
        }

        propertyGroup.AddProperty(PropertyType.TargetFramework.GetItemName(), TargetFramework);
        propertyGroup.AddProperty(PropertyType.RootNamespace.GetItemName(), projectNamespace);
        propertyGroup.AddProperty(PropertyType.HomeGuid.GetItemName(), homeGuid.ToString());
        propertyGroup.AddProperty(PropertyType.PackageId.GetItemName(), packageId.ToString());
        propertyGroup.AddProperty(PropertyType.AssemblyName.GetItemName(), UnevaluatedVariable(GetItemName(PropertyType.RootNamespace)));
        propertyGroup.AddProperty("DefaultItemExcludes", @"$(DefaultItemExcludes);Export\**;bin\**;obj\**;.temp\**;.meta\**;Assets\**;Screenshots\**;Render\**");
    }

    private static void AddDefaultReferenceGroup(this ProjectRootElement project)
    {
        var itemGroup = project.AddItemGroup();
        foreach (var reference in _defaultReferences)
        {
            var item = itemGroup.AddItem(reference.Type.GetItemName(), reference.Include);
            foreach (var tag in reference.Tags)
            {
                item.AddMetadata(tag.Tag.GetItemName(), tag.Value, tag.AddAsAttribute);
            }
        }
    }

    private static void AddDefaultContent(this ProjectRootElement project)
    {
        var contentTagName = GetItemName(ItemType.Content);

        foreach (var group in _defaultContent)
        {
            var itemGroup = project.AddItemGroup();
            itemGroup.Condition = group.Condition.ToString();
            foreach (var content in group.Content)
            {
                var item = itemGroup.AddItem(contentTagName, content.Include);
                if (content.TryGetExclude(out var exclude))
                    item.Exclude = exclude;

                foreach (var tag in content.GetTags())
                {
                    item.AddMetadata(tag.Tag.GetItemName(), tag.Value, tag.AddAsAttribute);
                }
            }
        }
    }

    private static void AddOpPackageItemGroup(this ProjectRootElement project)
    {
        var group = project.AddItemGroup();
        group.Label = OpPackItemGroupLabel;
    }

    private static void AddOperatorPackageTo(this ProjectRootElement project, string name, string @namespace, Version version, bool resourcesOnly)
    {
        // find item group
        var group = project.ItemGroups.FirstOrDefault(x => x.Label == OpPackItemGroupLabel);
        if (group == null)
        {
            group = project.AddItemGroup();
            group.Label = OpPackItemGroupLabel;
        }

        // add item
        var item = group.AddItem(GetItemName(ItemType.OperatorPackage), name + '.' + @namespace);
        item.AddMetadata(nameof(OperatorPackageReference.Version), version.ToBasicVersionString(), true);
        item.AddMetadata(nameof(OperatorPackageReference.ResourcesOnly), resourcesOnly.ToString(), true);
    }

    internal static bool AddCleanBuildTarget(this ProjectRootElement project)
    {
        // find existing target
        const string targetName = "ClearBuildOutput";
        var target = project.Targets.FirstOrDefault(x => x.Name == targetName);
        if (target != null)
        {
            return false;
        }

        // create new target
        target = project.AddTarget(targetName);
        target.BeforeTargets = "BeforeBuild";
        target.AddTask("RemoveDir").SetParameter("Directories", "bin/$(Configuration)");
        return true;
        /*
<Target Name="ClearBuildOutput" BeforeTargets="BeforeBuild">
<!-- Clear previous files in the export folder -->
<RemoveDir Directories="bin/$(Configuration)"/>
</Target>  */
    }

    internal static void AddPackageInfoTarget(this ProjectRootElement project)
    {
        var target = project.AddTarget("CreatePackageInfo");
        target.AfterTargets = "AfterBuild";

        const string perPackageInfoTagName = "OpPackageInfoJson";

        const string version = nameof(OperatorPackageReference.Version);
        const string resourcesOnly = nameof(OperatorPackageReference.ResourcesOnly);
        const string includeVar = nameof(OperatorPackageReference.Identity); // dont ask me, this is just how MSBuild works. I guess the "Include" tag is
        // basically the identity of the item?
        
        const string jsonStructure = $$"""
                                       {
                                           "{{includeVar}}": "%({{includeVar}})",
                                           "{{version}}": "%({{version}})", 
                                           "{{resourcesOnly}}": "%({{resourcesOnly}})"
                                       }
                                       """;

        const string opReferencesArray = "OperatorReferenceArray";
        const string jsonArrayIterator = $"@({OpPackIncludeTagName} -> '%({perPackageInfoTagName})', ',')";

        var itemGroup = target.AddItemGroup();
        itemGroup.Label = "Define json structure of referenced operator packages";
        var item = itemGroup.AddItem(OpPackIncludeTagName, UnevaluatedIterator(GetItemName(ItemType.OperatorPackage)));
        item.AddMetadata(perPackageInfoTagName, jsonStructure, false);

        var propertyGroup = target.AddPropertyGroup();
        propertyGroup.AddProperty(opReferencesArray, jsonArrayIterator);

        const string fullJsonTagName = "OperatorPackageInfoJson";
        var homeGuidPropertyName = GetItemName(PropertyType.HomeGuid);
        var packageIdPropertyName = GetItemName(PropertyType.PackageId);
        var rootNamespacePropertyName = GetItemName(PropertyType.RootNamespace);
        var editorVersionPropertyName = GetItemName(PropertyType.EditorVersion);
        
        propertyGroup.AddProperty(fullJsonTagName, $$"""
                                                     {
                                                         "{{nameof(ReleaseInfoSerialized.HomeGuid)}}": "{{UnevaluatedVariable(homeGuidPropertyName)}}", 
                                                         "{{nameof(ReleaseInfoSerialized.PackageId)}}": "{{UnevaluatedVariable(packageIdPropertyName)}}",
                                                         "{{nameof(ReleaseInfoSerialized.RootNamespace)}}": "{{UnevaluatedVariable(rootNamespacePropertyName)}}",
                                                         "{{nameof(ReleaseInfoSerialized.AssemblyFileName)}}": "{{UnevaluatedVariable(GetItemName(PropertyType.RootNamespace))}}",
                                                         "{{nameof(ReleaseInfoSerialized.Version)}}": "{{UnevaluatedVariable(GetItemName(PropertyType.VersionPrefix))}}",
                                                         "{{nameof(ReleaseInfoSerialized.EditorVersion)}}": "{{UnevaluatedVariable(editorVersionPropertyName)}}",
                                                         "{{nameof(ReleaseInfoSerialized.IsEditorOnly)}}": "{{UnevaluatedVariable(GetItemName(PropertyType.IsEditorOnly))}}",
                                                         "{{nameof(ReleaseInfoSerialized.OperatorPackages)}}": [{{UnevaluatedVariable(opReferencesArray)}}
                                                         ]
                                                     }
                                                     """);

        const string outputPathVariable = "OutputPath"; // built-in variable to get the output path of the project

        var task = target.AddTask("WriteLinesToFile");
        task.SetParameter("File", UnevaluatedVariable(outputPathVariable) + '/' + ReleaseInfo.FileName);
        task.SetParameter("Lines", UnevaluatedVariable(fullJsonTagName));
        task.SetParameter("Overwrite", "True");
        task.SetParameter("Encoding", "UTF-8");
    }

    #region Target Framework
    private const string NetVersion = RuntimeAssemblies.NetVersion;
    public const string TargetFramework = "net" + NetVersion;
    private const string TargetWindowsFramework = TargetFramework + "-windows";
    private const string TargetMacOSFramework = TargetFramework + "-macos";
    private const string TargetLinuxFramework = TargetFramework + "net9.0-linux";

    public static bool FrameworkIsCurrent(string framework)
    {
        return framework is TargetFramework or TargetWindowsFramework or TargetMacOSFramework or TargetLinuxFramework;
    }

    private static readonly Regex _netVersionRegex = GetVersionRegex();

    // generate regex replacing the number in .net versions, e.g. "net8.0-windows" -> "net9.0-windows" or "net6.0" -> "net6.0" or "net11.0-linux" -> "net9.0-linux"
    [GeneratedRegex(@"net(\d+\.\d+)-")]
    private static partial Regex GetVersionRegex();

    public static string UpdateFramework(string framework)
    {
        if (_netVersionRegex.IsMatch(framework))
        {
            return _netVersionRegex.Replace(framework, NetVersion);
        }

        return TargetFramework;
    }
    #endregion Target Framework

    public static string UnevaluatedVariable(string variableName) => $"$({variableName})";
    public static string UnevaluatedIterator(string variableName) => $"@({variableName})";

    private const string OpPackIncludeTagName = "OpPacks";
    private const string OpPackItemGroupLabel = "OperatorPackages";

    // we implement this to keep the method of getting xml element names consistent. Avoids direct "ToString" calls to avoid ambiguity and bugs between
    // getting the name of each type of enum correctly
    public static string GetItemName(this ItemType itemType) => _itemTypeNames[itemType];
    public static string GetItemName(this MetadataTagType tagType) => tagType.ToString();
    public static string GetItemName(this PropertyType propertyType) => propertyType.ToString();

    /// <summary>
    /// We need this collection to logically differentiate between standard Dll references and editor dll references
    /// </summary>
    private static readonly FrozenDictionary<ItemType, string> _itemTypeNames = new[]
            {
                (Name: nameof(ItemType.Content), Type: ItemType.Content),
                (Name: nameof(ItemType.ProjectReference),
                 Type: ItemType.ProjectReference),
                (Name: nameof(ItemType.PackageReference),
                 Type: ItemType.PackageReference),
                (Name: "Reference", Type: ItemType.DllReference),
                (Name: "Reference", Type: ItemType.EditorReference),
                (Name: "Operators", Type: ItemType.OperatorPackage)
            }
       .ToFrozenDictionary(x => x.Type, x => x.Name);

    private static readonly FrozenDictionary<PropertyType, string> _defaultProperties =
        new[]
                {
                    (Type: PropertyType.DisableTransitiveProjectReferences, Value: "true"),
                    (Type: PropertyType.VersionPrefix, Value: "1.0.0"),
                    (Type: PropertyType.Nullable, Value: "enable"),
                    (Type: PropertyType.EditorVersion, Value: Program.Version.ToBasicVersionString()),
                    (Type: PropertyType.IsEditorOnly, Value: "false"),
                    (Type: PropertyType.ImplicitUsings, Value: "disabled"),
                    (Type: PropertyType.Deterministic, Value: "true")
                }
           .ToFrozenDictionary(keySelector: x => x.Type, elementSelector: x => x.Value);

    private static readonly TagValue[] _defaultReferenceTags = [new TagValue(Tag: MetadataTagType.Private, Value: "false", AddAsAttribute: true)];

    private static readonly Reference[] _defaultReferences =
        [
            new Reference(type: ItemType.EditorReference, include: "Core.dll", tags: _defaultReferenceTags),
            new Reference(type: ItemType.EditorReference, include: "Logging.dll", tags: _defaultReferenceTags),
            new Reference(type: ItemType.EditorReference, include: "SharpDX.dll", tags: _defaultReferenceTags),
            new Reference(type: ItemType.EditorReference, include: "SharpDX.Direct3D11.dll", tags: _defaultReferenceTags),
            new Reference(type: ItemType.EditorReference, include: "SharpDX.DXGI.dll", tags: _defaultReferenceTags),
            new Reference(type: ItemType.EditorReference, include: "SharpDX.Direct2D1.dll", tags: _defaultReferenceTags),
        ];

    // Note : we are trying to stay platform-agnostic with directories, and so we use unix path separators
    private static readonly Condition _releaseConfigCondition = new(ConditionVarName: "Configuration", RequiredValue: "Release", IfEqual: true);
    private const string IncludeAllStr = "**/*";

    private static readonly string[] _excludeFoldersFromOutput =
            [
                CreateIncludePath(args: ["bin", IncludeAllStr]), 
                CreateIncludePath(args: ["obj", IncludeAllStr]),
                CreateIncludePath("Export", IncludeAllStr),      
                CreateIncludePath("Screenshots", IncludeAllStr), 
                CreateIncludePath("Render", IncludeAllStr),
                CreateIncludePath(".meta", IncludeAllStr),
                CreateIncludePath(".temp", IncludeAllStr),
            ];

    private const string FileIncludeFmt = IncludeAllStr + @"{0}";
    internal const string DependenciesFolder = FileLocations.DependenciesFolder;

    private static readonly ContentInclude.Group[] _defaultContent =
        [
            new ContentInclude.Group(Condition: null, Content: new ContentInclude(include: CreateIncludePath(args: [".", DependenciesFolder, IncludeAllStr]))),
            new ContentInclude.Group(Condition: _releaseConfigCondition, Content:
                [
                    new ContentInclude(include: CreateIncludePath(args: [FileLocations.AssetsSubfolder, IncludeAllStr]),
                                       linkDirectory: FileLocations.AssetsSubfolder,
                                       exclude: _excludeFoldersFromOutput),
                    new ContentInclude(include: string.Format(format: FileIncludeFmt, arg0: SymbolPackage.SymbolExtension),
                                       linkDirectory: FileLocations.ReleaseSymbolsSubfolder,
                                       exclude: _excludeFoldersFromOutput),
                    new ContentInclude(include: string.Format(format: FileIncludeFmt, arg0: EditorSymbolPackage.SymbolUiExtension),
                                       linkDirectory: FileLocations.SymbolUiSubFolder,
                                       exclude: _excludeFoldersFromOutput),
                    new ContentInclude(include: string.Format(format: FileIncludeFmt, arg0: EditorSymbolPackage.SourceCodeExtension),
                                       linkDirectory: FileLocations.SourceCodeSubFolder,
                                       exclude: _excludeFoldersFromOutput)
                ])
        ];

    private static string CreateIncludePath(params string[] args) => string.Join(separator: AssetRegistry.PathSeparator, value: args);

    private readonly record struct Using(string Name, string? Alias = null, bool Static = false);

    // Goal: No operator should need a Using statement for a slot type or operator duplication
    private static readonly Using[] _defaultUsingStatements =
        [
            // default System includes
            new(Name: "System"),
            new(Name: "System.Numerics"),
            new(Name: "System.Linq"),
            new(Name: "System.Linq.Enumerable", Static: true),
            new(Name: "System.Collections"),
            new(Name: "System.Linq.Expressions"),
            new(Name: "System.Collections.Generic"),
            new(Name: "System.Text"),
            new(Name: "System.Net"),
            new(Name: "System.Net.Http"),
            new(Name: "System.Threading.Tasks"),
            new(Name: "System.IO"),

            // T3 convenience includes
            new(Name: "T3.Core.Logging"),
            new(Name: "System.Runtime.InteropServices"),
            new(Name: "T3.Core.Operator"),
            new(Name: "T3.Core.Operator.Attributes"),
            new(Name: "T3.Core.Operator.Slots"),
            new(Name: "T3.Core.DataTypes"),
            new(Name: "T3.Core.Operator.Interfaces"),
            new(Name: "T3.Core.Resource"),

            // SharpDX types
            new(Name: "SharpDX.Direct3D11.Buffer", Alias: "Buffer"),
            new(Name: "SharpDX.Direct3D11.ShaderResourceView", Alias: "ShaderResourceView"),
            new(Name: "SharpDX.Direct3D11.UnorderedAccessView", Alias: "UnorderedAccessView"),
            new(Name: "SharpDX.Direct3D11.CullMode", Alias: "CullMode"),
            new(Name: "SharpDX.Direct3D11.FillMode", Alias: "FillMode"),
            new(Name: "SharpDX.Direct3D11.TextureAddressMode", Alias: "TextureAddressMode"),
            new(Name: "SharpDX.Direct3D11.Filter", Alias: "Filter"),
            new(Name: "SharpDX.DXGI.Format", Alias: "Format"),
            new(Name: "SharpDX.Direct3D11.Texture2DDescription", Alias: "Texture2DDescription"),
            new(Name: "SharpDX.Direct3D11.Texture3DDescription", Alias: "Texture3DDescription"),
            new(Name: "SharpDX.Direct3D11.RenderTargetBlendDescription", Alias: "RenderTargetBlendDescription"),
            new(Name: "SharpDX.Direct3D11.SamplerState", Alias: "SamplerState"),
            new(Name: "SharpDX.Direct3D11.UnorderedAccessViewBufferFlags", Alias: "UnorderedAccessViewBufferFlags"),
            new(Name: "SharpDX.Mathematics.Interop.RawRectangle", Alias: "RawRectangle"),
            new(Name: "SharpDX.Mathematics.Interop.RawViewportF", Alias: "RawViewportF"),
            new(Name: "SharpDX.Direct3D11.ResourceUsage", Alias: "ResourceUsage"),
            new(Name: "SharpDX.Direct3D11.ResourceOptionFlags", Alias: "ResourceOptionFlags"),
            new(Name: "SharpDX.Direct3D11.InputLayout", Alias: "InputLayout"),
            new(Name: "SharpDX.Direct3D.PrimitiveTopology", Alias: "PrimitiveTopology"),
            new(Name: "SharpDX.Direct3D11.BlendState", Alias: "BlendState"),
            new(Name: "SharpDX.Direct3D11.Comparison", Alias: "Comparison"),
            new(Name: "SharpDX.Direct3D11.BlendOption", Alias: "BlendOption"),
            new(Name: "SharpDX.Direct3D11.BlendOperation", Alias: "BlendOperation"),
            new(Name: "SharpDX.Direct3D11.BindFlags", Alias: "BindFlags"),
            new(Name: "SharpDX.Direct3D11.ColorWriteMaskFlags", Alias: "ColorWriteMaskFlags"),
            new(Name: "SharpDX.Direct3D11.CpuAccessFlags", Alias: "CpuAccessFlags"),
            new(Name: "SharpDX.Direct3D11.DepthStencilView", Alias: "DepthStencilView"),
            new(Name: "SharpDX.Direct3D11.DepthStencilState", Alias: "DepthStencilState"),
            new(Name: "SharpDX.Direct3D11.RenderTargetView", Alias: "RenderTargetView"),
            new(Name: "SharpDX.Direct3D11.RasterizerState", Alias: "RasterizerState"),

            // T3 types
            new(Name: "T3.Core.DataTypes.Point", Alias: "Point"),
            new(Name: "T3.Core.DataTypes.Texture2D", Alias: "Texture2D"),
            new(Name: "T3.Core.DataTypes.Texture3D", Alias: "Texture3D"),
            new(Name: "System.Numerics.Vector2", Alias: "Vector2"),
            new(Name: "System.Numerics.Vector3", Alias: "Vector3"),
            new(Name: "System.Numerics.Vector4", Alias: "Vector4"),
            new(Name: "System.Numerics.Matrix4x4", Alias: "Matrix4x4"),
            new(Name: "System.Numerics.Quaternion", Alias: "Quaternion"),
            new(Name: "T3.Core.DataTypes.Vector.Int2", Alias: "Int2"),
            new(Name: "T3.Core.DataTypes.Vector.Int3", Alias: "Int3"),
            new(Name: "T3.Core.DataTypes.Vector.Int4", Alias: "Int4"),
            new(Name: "T3.Core.Resource.ResourceManager", Alias: "ResourceManager"),
            new(Name: "T3.Core.DataTypes.ComputeShader", Alias: "ComputeShader"),
            new(Name: "T3.Core.DataTypes.PixelShader", Alias: "PixelShader"),
            new(Name: "T3.Core.DataTypes.VertexShader", Alias: "VertexShader"),
            new(Name: "T3.Core.DataTypes.GeometryShader", Alias: "GeometryShader")
        ];
}

#region CsProjectFile Xml Tag Types
internal enum PropertyType
{
    RootNamespace,
    TargetFramework,
    DisableTransitiveProjectReferences,
    VersionPrefix,
    Nullable,
    HomeGuid,
    PackageId,
    EditorVersion,
    AssemblyName,
    IsEditorOnly,
    ImplicitUsings,
    Deterministic, 
    OutputPath
}

internal enum ItemType
{
    ProjectReference,
    PackageReference,
    DllReference,

    /// <summary>
    /// Not technically a type seen in MSBuild, but used in T3 to refer to assemblies references from the t3 editor at runtime
    /// using <see cref="ProjectSetup.EnvironmentVariableName"/>. See <see cref="ProjectXml._itemTypeNames"/>.
    /// </summary>
    EditorReference,
    OperatorPackage,
    Content
}

internal enum MetadataTagType
{
    Include,
    Exclude,
    Link,
    CopyToOutputDirectory,
    Private,
    Label,
}
#endregion CsProjectFile Xml Tag Types
#region Utility Types for consistent handling
internal readonly record struct TagValue(MetadataTagType Tag, string Value, bool AddAsAttribute);

internal readonly struct Reference(ItemType type, string include, params TagValue[]? tags)
{
    public readonly ItemType Type = type;

    public readonly string Include = type == ItemType.EditorReference
                                         ? Path.Combine(ProjectXml.UnevaluatedVariable(ProjectSetup.EnvironmentVariableName), include)
                                         : include;

    public readonly TagValue[] Tags = tags ?? Array.Empty<TagValue>();
}

internal readonly record struct Condition(string ConditionVarName, string RequiredValue, bool IfEqual)
{
    public override string ToString() => $"'{ProjectXml.UnevaluatedVariable(ConditionVarName)}' {(IfEqual ? "==" : "!=")} '{RequiredValue}'";
}

internal readonly record struct ContentInclude
{
    public ContentInclude(string include, string? linkDirectory = null, string copyMode = "PreserveNewest", params string?[]? exclude)
    {
        _include = new TagValue(MetadataTagType.Include, include, true);

        linkDirectory = linkDirectory == null ? "" : linkDirectory + '/';
        _link = new TagValue(MetadataTagType.Link, linkDirectory + @"%(RecursiveDir)%(Filename)%(Extension)", false);

        _exclude = exclude is { Length: > 0 }
                       ? new TagValue(MetadataTagType.Exclude, string.Join(";", exclude), true)
                       : null;

        _copyMode = new TagValue(MetadataTagType.CopyToOutputDirectory, copyMode, true);
    }

    public IEnumerable<TagValue> GetTags()
    {
        yield return _link;
        yield return _copyMode;
    }

    public bool TryGetExclude([NotNullWhen(true)] out string? exclude)
    {
        exclude = _exclude?.Value;
        return _exclude is not null;
    }

    public string Include => _include.Value;
    private readonly TagValue _include;
    private readonly TagValue? _exclude;
    private readonly TagValue _link;
    private readonly TagValue _copyMode;

    public readonly record struct Group(Condition? Condition, params ContentInclude[] Content);
}
#endregion Utility Types for consistent handling