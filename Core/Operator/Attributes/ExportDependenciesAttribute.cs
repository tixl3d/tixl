using System;

namespace T3.Core.Operator.Attributes;

/// <summary>
/// Declares files in the operator package or player directory that only this operator needs (typically native
/// libraries). When an executable is exported, a file declared by any operator is shipped only if one of the
/// operators declaring it is part of the export. File names may use <c>*</c> as a wildcard, e.g. <c>"av*.dll"</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExportDependenciesAttribute(params string[] fileNames) : Attribute
{
    public string[] FileNames { get; } = fileNames;
}
