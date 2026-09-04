using System.CodeDom.Compiler;
using System.Collections.Concurrent;

namespace ProjectUpdater;

internal sealed partial class Conversion
{
    private const string DeprecatedNamespacePrefix = "namespace T3.Operators.Types.Id_";
    private const string NamespacePrefix = "namespace ";
    
    private static readonly CodeDomProvider CodeDomProvider = CodeDomProvider.CreateProvider("C#");
    
    private readonly ConcurrentDictionary<Guid, string> _destinationDirectories = new();
    private readonly List<NamespaceChanged> _changedNamespaces = [];

    public void StartConversion(string rootDirectory, string newRootDirectory)
    {
        EnumerateFileOperations(rootDirectory, newRootDirectory, "*.cs", ConvertAndMoveCSharp);
        EnumerateFileOperations(rootDirectory, newRootDirectory, "*.t3", ConvertAndMoveT3File);
        EnumerateFileOperations(rootDirectory, newRootDirectory, "*.t3ui", ConvertAndMoveT3File);
        EnumerateFileOperations(rootDirectory, newRootDirectory, "*.hlsl", ConvertShaderFile);

        var changedNamespaces = _changedNamespaces.ToArray();
        Directory.EnumerateFiles(newRootDirectory, "*.cs", SearchOption.AllDirectories)
            .AsParallel()
            .ForAll(file =>
            {
                using var reader = new StreamReader(file);
                var fileContents = reader.ReadToEnd();
                reader.Close();

                for (var index = 0; index < changedNamespaces.Length; index++)
                {
                    NamespaceChanged namespaceChanged = changedNamespaces[index];
                    fileContents = fileContents.Replace(namespaceChanged.OldNamespace, namespaceChanged.NewNamespace);
                }

                File.WriteAllText(file, fileContents);
            });
    }

    private void EnumerateFileOperations(string rootDirectory, string newRootDirectory, string searchPattern, FileOperationDelegate action)
    {
        Directory.EnumerateFiles(rootDirectory, searchPattern, SearchOption.AllDirectories)
            .AsParallel()
            .ForAll(filePath =>
            {
                var reader = new StreamReader(filePath);
                var fileContents = reader.ReadToEnd();
                reader.Dispose();
                
                var changedFile = action(filePath: filePath, 
                    fileContents: fileContents,  
                    originalRootDirectory: rootDirectory, 
                    newRootDirectory: newRootDirectory);

                if (changedFile == FileChangeInfo.Empty)
                    return;
                
                var directory = Path.GetDirectoryName(changedFile.NewFilePath)!;
                try
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(changedFile.NewFilePath, changedFile.NewFileContents);
                }
                catch (IOException e)
                {
                    Console.WriteLine($"Could not write to file: \"{changedFile.NewFilePath}\": {e}");
                    return;
                }
                
                //Console.WriteLine($"Converted {filePath} -> {changedFile.NewFilePath}");

                try
                {
                    File.Delete(filePath);
                }
                catch (IOException e)
                {
                    Console.WriteLine($"Could not delete original file: \"{filePath}\": {e}");
                }
            });
    }

    private delegate FileChangeInfo FileOperationDelegate(string filePath, string fileContents, string originalRootDirectory, string newRootDirectory);
}