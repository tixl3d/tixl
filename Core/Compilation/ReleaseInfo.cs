using System;
using System.Linq;
using T3.Core.Logging;

namespace T3.Core.Compilation;

[Serializable]
public readonly record struct OperatorPackageReferenceSerialized(string Identity, string Version, bool ResourcesOnly);

[Serializable]
// Warning: Do not change these structs, as they are used in the serialization of the operator package file and is linked to the csproj json output
public record ReleaseInfoSerialized(
    string AssemblyFileName,
    Guid HomeGuid,
    Guid PackageId,
    string RootNamespace,
    string EditorVersion,
    string Version,
    bool IsEditorOnly,
    OperatorPackageReferenceSerialized[] OperatorPackages);

// Identity must equal that package's root namespace
public sealed record OperatorPackageReference(string Identity, Version Version, bool ResourcesOnly);

public sealed record ReleaseInfo(
    string AssemblyFileName,
    Guid HomeGuid,
    Guid PackageId,
    string RootNamespace,
    Version EditorVersion,
    Version Version,
    bool IsEditorOnly,
    OperatorPackageReference[] OperatorPackages)
{
    
    public const string FileName = "OperatorPackage.json";
}

internal static class ReleaseInfoExtensions
{
    internal static ReleaseInfo ToReleaseInfo(this ReleaseInfoSerialized serialized)
    {
        if (!Version.TryParse(serialized.EditorVersion, out var editorVersion))
        {
            editorVersion = new Version(1, 0, 0);
            Log.Error($"{serialized.RootNamespace}: Failed to parse editor version \"{serialized.EditorVersion}\" from package info. Setting to {editorVersion}");
        }
        
        if (!Version.TryParse(serialized.Version, out var version))
        {
            version = new Version(1, 0, 0);
            Log.Error($"{serialized.RootNamespace}: Failed to parse package version \"{serialized.Version}\" from package info. Setting to {version}");
        }
        
        return new ReleaseInfo(
                               serialized.AssemblyFileName,
                               serialized.HomeGuid,
                               serialized.PackageId,
                               serialized.RootNamespace,
                               editorVersion,
                               version,
                               serialized.IsEditorOnly,
                               serialized.OperatorPackages
                                         .Select(x => new OperatorPackageReference(x.Identity, new Version(x.Version), x.ResourcesOnly))
                                         .ToArray());
    }
}