using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

// Arguments: <Lib.dll> <dependency folder>... -- <operation and its arguments>
var separatorIndex = Array.IndexOf(args, "--");
if (args.Length < 2 || separatorIndex < 1)
{
    Console.Error.Write("Usage: AssimpImportHost <operator package dll> <dependency folders...> -- <operation arguments>");
    return 64;
}

var packageAssemblyPath = args[0];
var dependencyFolders = args[1..separatorIndex];
var operationArguments = args[(separatorIndex + 1)..];

try
{
    AssemblyLoadContext.Default.Resolving += (context, name) =>
                                             {
                                                 foreach (var folder in dependencyFolders)
                                                 {
                                                     var candidate = Path.Combine(folder, name.Name + ".dll");
                                                     if (File.Exists(candidate))
                                                         return context.LoadFromAssemblyPath(candidate);
                                                 }

                                                 return null;
                                             };

    var packageAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(packageAssemblyPath));
    var entryType = packageAssembly.GetType("Lib.Utils.AssimpIo.AssimpIsolatedImport", throwOnError: true)!;
    var entryMethod = entryType.GetMethod("RunChild", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                      ?? throw new MissingMethodException(entryType.FullName, "RunChild");

    return (int)entryMethod.Invoke(null, [operationArguments])!;
}
catch (Exception e)
{
    // A missing assembly or a breaking change between host and package; the parent shows this text
    Console.Error.Write(e.GetBaseException().Message);
    return 65;
}
