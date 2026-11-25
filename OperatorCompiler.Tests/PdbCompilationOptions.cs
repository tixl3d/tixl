using System.Reflection.Metadata;
using System.Text;

namespace OperatorCompiler.Tests;

/// <summary>The compiler options a portable PDB records about its build (defines, nullable, optimization, ...).</summary>
internal static class PdbCompilationOptions
{
    public static Dictionary<string, string> Read(string pdbPath)
    {
        using var stream = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        var options = new Dictionary<string, string>();
        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var info = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(info.Kind) != _compilationOptionsKind)
                continue;

            var parts = Encoding.UTF8.GetString(reader.GetBlobBytes(info.Value)).Split('\0');
            for (var index = 0; index + 1 < parts.Length; index += 2)
                options[parts[index]] = parts[index + 1];
        }

        return options;
    }

    private static readonly Guid _compilationOptionsKind = new("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");
}
