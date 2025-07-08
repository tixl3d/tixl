#nullable enable
using System;
using System.IO;
using System.Reflection;

namespace T3.Core.Compilation;

/// <summary>
/// This is a common info container for package loading and versioning purposes.
/// </summary>
public static class RuntimeAssemblies
{
    private static readonly Assembly _coreAssembly = typeof(RuntimeAssemblies).Assembly;
    public static readonly string CoreDirectory = Path.GetDirectoryName(_coreAssembly.Location)!;
    public static readonly Version Version = _coreAssembly.GetName().Version!;
    public const string NetVersion = "9.0";
}

