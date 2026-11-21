#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace T3.Core.Utils;

/// <summary>
/// Runs the isolated Assimp worker inside the editor executable. The operator passes the Lib package
/// and its dependency folders, exactly like the standalone AssimpImportHost launcher, so a
/// self-contained editor without a <c>dotnet</c> executable can still run imports and exports in a
/// separate process.
/// </summary>
/// <remarks>
/// <see cref="TryRun"/> must be called at the very start of the application entry point, before any
/// editor, graphics or settings initialization. It returns false for every normal launch.
/// </remarks>
public static class IsolatedAssimpHost
{
    /// <summary>First argument that tells the editor to act as the worker instead of starting the UI.</summary>
    public const string Flag = "--assimp-host";

    /// <summary>
    /// Detects the worker flag and, if present, runs the operation in this process. Returns true when
    /// this launch is a worker, with <paramref name="exitCode"/> set for the caller to exit with.
    /// </summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Flag)
            return false;

        try
        {
            var separatorIndex = Array.IndexOf(args, "--");
            if (args.Length < 3 || separatorIndex < 2)
            {
                Console.Error.Write($"Usage: TiXL {Flag} <operator package dll> <dependency folders...> -- <operation arguments>");
                exitCode = 64;
                return true;
            }

            var packageAssemblyPath = args[1];
            var dependencyFolders = args[2..separatorIndex];
            var operationArguments = args[(separatorIndex + 1)..];

            // Resolve the operator package's dependencies from the folders the parent passed; the
            // worker must not depend on assemblies already loaded by the editor.
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

            exitCode = (int)entryMethod.Invoke(null, [operationArguments])!;
            return true;
        }
        catch (Exception e)
        {
            // A missing assembly or a breaking change between the editor and the package
            Console.Error.Write(e.GetBaseException().Message);
            exitCode = 65;
            return true;
        }
    }
}
