#nullable enable
using System.IO;
using System.Threading;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.UserData;

namespace T3.Editor.UiModel.Exporting;

internal static partial class PlayerExporter
{
    private sealed class ExportData
    {
        public IReadOnlyCollection<ExportDataFile> ExportDataFiles => _exportDataFiles;
        public IEnumerable<SymbolPackage> SymbolPackages => _symbolPackages.Keys;

        private readonly HashSet<Symbol> _symbols = new();
        private readonly Dictionary<SymbolPackage, List<Symbol>> _symbolPackages = new();
        private readonly HashSet<ExportDataFile> _exportDataFiles = new();
        private readonly HashSet<Instance> _collectedInstances = new();

        public bool TryAddInstance(Instance instance) => _collectedInstances.Add(instance);

        public void TryAddResourcePath(in ExportDataFile path) => _exportDataFiles.Add(path);

        public bool TryAddSymbol(Symbol symbol)
        {
            Console.WriteLine("Including symbol: " + symbol.Name);
            if(!_symbols.Add(symbol))
                return false;
            
            var package = symbol.SymbolPackage;
            if (!_symbolPackages.TryGetValue(package, out var symbols))
            {
                symbols = new List<Symbol>();
                _symbolPackages.Add(package, symbols);
            }
            
            symbols.Add(symbol);

            foreach(var child in symbol.Children.Values)
            {
                TryAddSymbol(child.Symbol);
            }

            return true;
        }

        public void PrintInfo()
        {
            Log.Info($"Collected {_collectedInstances.Count} instances for export in {_symbols.Count} different symbols:");
            foreach (var resourcePath in ExportDataFiles)
            {
                Log.Info($"  {resourcePath}");
            }
        }

        public bool TryAddSharedResource(string resourcePath, IReadOnlyList<IResourcePackage>? otherDirs = null, FileResource? fileResource= null)
        {
            IResourcePackage[] resources  = fileResource?.ResourcePackage != null 
                                                 ? [fileResource.ResourcePackage] 
                                                 : [];
            
            var searchDirs = otherDirs ?? resources;
            
            var tempResourceConsumer = new TempResourceConsumer(searchDirs);
            if (!AssetRegistry.TryResolveAddress(resourcePath, tempResourceConsumer, out var absolutePath, out var package))
            {
                Log.Error($"Can't find file: {resourcePath}");
                return false;
            }
            
            resourcePath = resourcePath.Replace("\\", "/");
            absolutePath = absolutePath.Replace("\\", "/");
            
            
            if (package == null)
            {
                Log.Error($"Can't add resource file without package: {resourcePath}");
                return false;
            }
            
            var relativePathInResourceFolder = Path.GetRelativePath(package.AssetsFolder, absolutePath);
            TryAddResourcePath(new ExportDataFile(package.RootNamespace, relativePathInResourceFolder, absolutePath));

            // Copy related font textures
            if (resourcePath.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase))
            {
                var resourcePathPng = resourcePath.Replace(".fnt", ".png");
                var absolutePathPng = absolutePath.Replace(".fnt", ".png");
                var relativePathInResourceFolderPng = relativePathInResourceFolder.Replace(".fnt", ".png");
                
                TryAddResourcePath(new ExportDataFile(package.RootNamespace,relativePathInResourceFolderPng, absolutePathPng));
            }

            // search for shader includes
            if (absolutePath.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(absolutePath);
                ShaderCompiler.ShaderResourcePackage shaderResourcePackage = new(fileInfo);
                var shaderDirs = searchDirs.Append(shaderResourcePackage).Distinct().ToArray();
                var shaderText = File.ReadAllText(absolutePath);
                foreach (var includePath in ShaderCompiler.GetIncludesFrom(shaderText))
                {
                    TryAddSharedResource(includePath, shaderDirs);
                }
            }

            return true;
        }
    }
    
    
    private sealed class ExportDataFile(string? packageRootNamespace, string relativePathInResourcesFolder, string absolutePath)
    {
        private readonly string? _packageRootNamespace = packageRootNamespace;
        private readonly string _relativePathInResourcesFolder = relativePathInResourcesFolder;
        private readonly string _absolutePath = absolutePath;

        // equality operators
        public static bool operator ==(ExportDataFile left, ExportDataFile right) => left._absolutePath == right._absolutePath;
        public static bool operator !=(ExportDataFile left, ExportDataFile right) => left._absolutePath != right._absolutePath;
        public override int GetHashCode() => _absolutePath.GetHashCode();
        public override bool Equals(object? obj) => obj is ExportDataFile other && other == this;

        public override string ToString() => $"\"{_relativePathInResourcesFolder}\" (\"{_absolutePath}\")";

        private bool TryCopy(ref int successInt, string exportDir)
        {
            //var targetPath = Path.Combine(exportDir, resourcePath.RelativePath);
            var targetPath = GetTargetPathFolder(exportDir);
            var success = TryCopyFile(_absolutePath, targetPath);

            // Use bit operations to et successInt to 0 on failure
            Interlocked.And(ref successInt, Convert.ToInt32(success));
            if (!success)
            {
                Log.Error($"Failed to copy resource file for export: {_absolutePath}");
                return false;
            }

            return true;
        }

        private string GetTargetPathFolder(string exportDir)
        {
            if (_packageRootNamespace != null)
            {
                return Path.Combine(exportDir, "Operators",
                                    _packageRootNamespace,
                                    FileLocations.AssetsSubfolder,
                                    _relativePathInResourcesFolder);
            }

            return Path.Combine(exportDir, _relativePathInResourcesFolder);
        }

        public static bool TryCopyToExportDir(IEnumerable<ExportDataFile> resourcePaths, string exportDir)
        {
            var successInt = Convert.ToInt32(true);
            resourcePaths
               .AsParallel()
               .ForAll(resourcePath => resourcePath.TryCopy(ref successInt ,exportDir));

            return Convert.ToBoolean(successInt);
        }
    }
}