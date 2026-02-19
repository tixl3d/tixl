#nullable enable
using System.IO;
using System.Threading;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Editor.UiModel.Exporting;

internal static partial class PlayerExporter
{
    private sealed class ExportData
    {
        public IReadOnlyCollection<AssetExportItem> ExportItems => _exportItems;
        
        private readonly HashSet<Symbol> _symbols = [];
        private readonly HashSet<Instance> _collectedInstances = [];
        
        /** Packages with code */
        public IEnumerable<SymbolPackage> SymbolPackages => _symbolPackages.Keys;
        private readonly Dictionary<SymbolPackage, List<Symbol>> _symbolPackages = new();

        /** Packages including used assets */
        public readonly HashSet<IResourcePackage> AssetPackages = [];
        
        private readonly HashSet<AssetExportItem> _exportItems = [];

        public bool TryAddInstance(Instance instance) => _collectedInstances.Add(instance);

        public void TryAddExportAsset(in AssetExportItem exportItem)
        {
            _exportItems.Add(exportItem);
        }

        /// <summary>
        /// Collect <see cref="Symbol"/> and its <see cref="SymbolPackage"/>. 
        /// </summary>
        public bool TryAddSymbol(Symbol symbol)
        {
            Console.WriteLine("Including symbol: " + symbol.Name);
            if(!_symbols.Add(symbol))
                return false;
            
            var package = symbol.SymbolPackage;
            if (!_symbolPackages.TryGetValue(package, out var symbols))
            {
                symbols = [];
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
            foreach (var resourcePath in ExportItems)
            {
                Log.Debug($"  {resourcePath}");
            }
        }

        /// <summary>
        /// Collect <see cref="Asset"/> and its <see cref="SymbolPackage"/>
        /// </summary>
        public bool TryAddSharedAsset(Asset asset)
        {
            var relativePathInResourceFolder = Path.GetRelativePath(asset.Package.AssetsFolder, asset.FullPath);
            TryAddExportAsset(new AssetExportItem(asset.Package.RootNamespace, relativePathInResourceFolder, asset.FullPath));

            // Include related font textures
            if (asset.Address.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase))
            {
                var absolutePathPng = asset.FullPath.Replace(".fnt", ".png");
                var relativePathInResourceFolderPng = relativePathInResourceFolder.Replace(".fnt", ".png");
                
                TryAddExportAsset(new AssetExportItem(asset.Package.RootNamespace,
                                                      relativePathInResourceFolderPng, 
                                                      absolutePathPng));
            }

            // Search and include for shader includes
            if (asset.Address.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
            {
                var shaderText = File.ReadAllText(asset.FullPath);
                foreach (var includePath in ShaderCompiler.GetIncludesFrom(shaderText))
                {
                    if (!ShaderCompiler.TryResolveSharedIncludeAsset(includePath, out var includeAsset))
                        continue;
                    
                    var relativePathInResourceFolder2 = Path.GetRelativePath(includeAsset.Package.AssetsFolder, includeAsset.FullPath);
                    TryAddExportAsset(new AssetExportItem(includeAsset.Package.RootNamespace, 
                                                          relativePathInResourceFolder2, 
                                                          includeAsset.FullPath));
                }
            }

            AssetPackages.Add(asset.Package);

            return true;
        }
    }
    
    
    private sealed class AssetExportItem(string? packageRootNamespace, string relativePathInResourcesFolder, string absolutePath)
    {
        private readonly string? _packageRootNamespace = packageRootNamespace;
        private readonly string _relativePathInResourcesFolder = relativePathInResourcesFolder;
        private readonly string _absolutePath = absolutePath;

        // equality operators
        public static bool operator ==(AssetExportItem left, AssetExportItem right) => left._absolutePath == right._absolutePath;
        public static bool operator !=(AssetExportItem left, AssetExportItem right) => left._absolutePath != right._absolutePath;
        public override int GetHashCode() => _absolutePath.GetHashCode();
        public override bool Equals(object? obj) => obj is AssetExportItem other && other == this;

        public override string ToString() => $"\"{_relativePathInResourcesFolder}\" (\"{_absolutePath}\")";

        private bool TryCopyTo(string exportDir, ref int successCount)
        {
            //var targetPath = Path.Combine(exportDir, resourcePath.RelativePath);
            var targetPath = GetTargetPathDir(exportDir);
            var success = TryCopyFile(_absolutePath, targetPath);

            // Use bit operations to et successInt to 0 on failure
            Interlocked.And(ref successCount, Convert.ToInt32(success));
            if (!success)
            {
                Log.Error($"Failed to copy resource file for export: {_absolutePath}");
                return false;
            }

            return true;
        }

        private string GetTargetPathDir(string exportDir)
        {
            if (_packageRootNamespace != null)
            {
                return Path.Combine(exportDir, FileLocations.OperatorsSubFolder,
                                    _packageRootNamespace,
                                    FileLocations.AssetsSubfolder,
                                    _relativePathInResourcesFolder);
            }

            return Path.Combine(exportDir, _relativePathInResourcesFolder);
        }

        public static bool TryCopyItems(IEnumerable<AssetExportItem> exportItems, string exportDir)
        {
            var successInt = Convert.ToInt32(true);
            exportItems
               .AsParallel()
               .ForAll(item => item.TryCopyTo(exportDir, ref successInt));

            return Convert.ToBoolean(successInt);
        }
    }
}