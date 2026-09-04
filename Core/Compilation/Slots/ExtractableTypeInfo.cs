#nullable enable
using System;

namespace T3.Core.Compilation;

/// <summary>
/// Used to organize type information about extractable slots in <see cref="AssemblyInformation"/>
/// </summary>
public readonly record struct ExtractableTypeInfo(bool IsExtractable, Type? ExtractableType);