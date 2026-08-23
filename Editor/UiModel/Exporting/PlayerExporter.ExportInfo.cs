#nullable enable
using System.IO;
using System.Threading;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Editor.UiModel.Exporting;

internal static partial class PlayerExporter
{
    /// <summary>
    /// Everything collected for one export: the instances reached from the output, the symbols and packages they
    /// belong to, and the asset files they reference.
    /// </summary>
    private sealed class ExportData(Symbol rootSymbol)
    {
        public IReadOnlyCollection<AssetExportItem> ExportItems => _exportItems;

        /** Packages with code */
        public IEnumerable<SymbolPackage> SymbolPackages => _symbolPackages.Keys;

        /** Symbols shipped with the export */
        public IEnumerable<Symbol> Symbols => _symbols;

        /** Packages including used assets */
        public readonly HashSet<IResourcePackage> AssetPackages = [];

        public bool StripsUnusedOperators { get; private set; }

        public bool TryAddInstance(Instance instance) => _collectedInstances.Add(instance);

        public void TryAddExportAsset(in AssetExportItem exportItem)
        {
            _exportItems.Add(exportItem);
        }

        /// <summary>
        /// Derives the shipped symbols from the collected instances. When stripping, only symbols of reached instances
        /// are shipped and each symbol remembers which of its children were reached; otherwise the complete static
        /// child graph of the root symbol is shipped, as it would be instantiated.
        /// </summary>
        public void FinishCollection(bool stripUnusedOperators)
        {
            StripsUnusedOperators = stripUnusedOperators;
            _symbols.Clear();
            _symbolPackages.Clear();
            _reachableChildIds.Clear();

            if (!stripUnusedOperators)
            {
                AddSymbolWithChildren(rootSymbol);
                return;
            }

            AddSymbol(rootSymbol);
            foreach (var instance in _collectedInstances)
            {
                AddSymbol(instance.Symbol);

                var parent = instance.Parent;
                if (parent == null)
                    continue;

                if (!_reachableChildIds.TryGetValue(parent.Symbol.Id, out var childIds))
                {
                    childIds = [];
                    _reachableChildIds.Add(parent.Symbol.Id, childIds);
                }

                childIds.Add(instance.SymbolChildId);
            }
        }

        public IEnumerable<Symbol> GetSymbolsOfPackage(SymbolPackage package)
        {
            return _symbolPackages.TryGetValue(package, out var symbols) ? symbols : [];
        }

        public IReadOnlySet<Guid> GetReachableChildIds(Symbol symbol)
        {
            return _reachableChildIds.TryGetValue(symbol.Id, out var childIds) ? childIds : _noChildIds;
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

        private void AddSymbolWithChildren(Symbol symbol)
        {
            if (!AddSymbol(symbol))
                return;

            foreach (var child in symbol.Children.Values)
            {
                AddSymbolWithChildren(child.Symbol);
            }
        }

        private bool AddSymbol(Symbol symbol)
        {
            if (!_symbols.Add(symbol))
                return false;

            var package = symbol.SymbolPackage;
            if (!_symbolPackages.TryGetValue(package, out var symbols))
            {
                symbols = [];
                _symbolPackages.Add(package, symbols);
            }

            symbols.Add(symbol);
            return true;
        }

        private static readonly HashSet<Guid> _noChildIds = [];
        private readonly HashSet<Symbol> _symbols = [];
        private readonly HashSet<Instance> _collectedInstances = [];
        private readonly Dictionary<SymbolPackage, List<Symbol>> _symbolPackages = new();
        private readonly Dictionary<Guid, HashSet<Guid>> _reachableChildIds = new();
        private readonly HashSet<AssetExportItem> _exportItems = [];
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

    /// <summary>
    /// Counts what a copy pass shipped and skipped, for the export summary.
    /// </summary>
    private sealed class CopyReport
    {
        public int CopiedCount { get; private set; }
        public long CopiedBytes { get; private set; }
        public int SkippedCount { get; private set; }
        public long SkippedBytes { get; private set; }

        public void Copy(string path)
        {
            CopiedCount++;
            CopiedBytes += GetLength(path);
        }

        public void Skip(string path)
        {
            SkippedCount++;
            SkippedBytes += GetLength(path);
        }

        private static long GetLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
