#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using T3.Editor.Compilation;
using T3.Editor.ExtensionPackages;
using T3.Editor.Gui.InputUi.SimpleInputUis;

namespace T3.Editor.UiModel.Helpers;

/// <summary>
/// Aggregates information about all symbols (warnings, dependencies, usage, examples),
/// and also provides single-symbol analysis for dialogs and tools.
/// </summary>
internal static class SymbolAnalysis
{
    /// <summary>
    /// Detailed info per symbol id, filled by UpdateDetails().
    /// </summary>
    internal static readonly Dictionary<Guid, SymbolInformation> InformationForSymbolIds = new(1000);

    internal static int TotalUsageCount;

    /// <summary>
    /// All connections between input slots stored as hashes (sourceSlotId x targetSlotId).
    /// Used by SymbolBrowser for relevancy weighting of frequent combinations.
    /// </summary>
    internal static Dictionary<int, int> ConnectionHashCounts = new();

    internal static bool DetailsInitialized { get; private set; }

    // Cache for usage counts (used by both bulk and single-symbol queries)
    private static readonly Dictionary<Guid, int> _latestUsageCounts = new();

    internal static void LogInvalidSymbolDependencies()
    {
        if (!DetailsInitialized)
            UpdateDetails();
        
        foreach (var symbol in SymbolRegistry.SymbolsFromAllPackages()
                                             .OrderBy(s => s.Namespace)
                                             .ThenBy(s => s.Name))
        {
            if (!InformationForSymbolIds.TryGetValue(symbol.Id, out var infos))
                continue;

            if (!_packageDependencies.TryGetValue(symbol.SymbolPackage.Name, out var deps))
                continue;

            foreach (var requiredSymbolId in infos.RequiredSymbolIds)
            {
                if (!SymbolRegistry.TryGetSymbol(requiredSymbolId, out var depSymbol))
                    continue;

                if (!deps.Contains(depSymbol.SymbolPackage.Name))
                {
                    Log.Warning($"{symbol.Namespace}.{symbol.Name}  depends on {depSymbol.Namespace}.{depSymbol.Name}");
                }
            }
        }
    }

    internal static void LogInvalidAssetReference()
    {
        foreach (var symbol in SymbolRegistry.SymbolsFromAllPackages()
                                             .OrderBy(s => s.Namespace)
                                             .ThenBy(s => s.Name))
        {
            if (!_packageDependencies.TryGetValue(symbol.SymbolPackage.Name, out var deps))
                continue;            
            
        
            
            // Symbol children
            foreach (var child in symbol.Children.Values)
            {
                foreach (var input in child.Inputs.Values)
                {
                    if (input.IsDefault)
                        continue;

                    if (input.InputDefinition.ValueType != typeof(string))
                        continue;

                    if (input.Value is not InputValue<string> stringValue)
                        continue;

                    var pathValue = stringValue.Value;
                    if (string.IsNullOrEmpty(pathValue))
                        continue;

                    if (!SymbolUiRegistry.TryGetSymbolUi(child.Symbol.Id, out var childSymbolUi))
                        continue;

                    if (!childSymbolUi.InputUis.TryGetValue(input.Id, out var inputUi))
                        continue;

                    if (inputUi is not StringInputUi stringUi)
                        continue;

                    
                    switch (stringUi.Usage)
                    {
                        case StringInputUi.UsageType.FilePath:
                        case StringInputUi.UsageType.DirectoryPath:
                        case StringInputUi.UsageType.OutputFilePath:
                        {
                            var assetPackageNameIndex = pathValue.IndexOf(':');
                            if (assetPackageNameIndex < 1)
                                break;

                            var assetPackageName = pathValue[..(assetPackageNameIndex)];
                            
                            if (!deps.Contains(assetPackageName))
                            {
                                Log.Warning($"{symbol.Namespace}.[{symbol.Name}].{childSymbolUi.Symbol.Name} references {pathValue}");
                            }    
                            break;
                        }
                    }
                }
            }
        }
    }


    private static readonly string[] _libPackages = [FileLocations.LibPackageName, FileLocations.TypesPackageName, "Mediapipe", "t3.ndi", "t3.spout", "t3.unsplash"];

    private static readonly Dictionary<string, string[]> _packageDependencies
        = new()
              {
                  { FileLocations.LibPackageName, [.._libPackages] },
                  { FileLocations.ExamplesPackageName, [.._libPackages, FileLocations.ExamplesPackageName] },
                  { FileLocations.SkillsPackageName, [.._libPackages, FileLocations.SkillsPackageName] }
              };

    /// <summary>
    /// Basic usage info used by symbol browser and relevancy search.
    /// </summary>
    internal static void UpdateSymbolUsageCounts()
    {
        Log.Debug("Updating symbol counts...");
        var usages = CollectSymbolUsageCounts();
        ConnectionHashCounts = new Dictionary<int, int>();

        foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
        {
            var symbolId = symbolUi.Symbol.Id;
            if (!InformationForSymbolIds.TryGetValue(symbolId, out var info))
            {
                info = new SymbolInformation();
                InformationForSymbolIds[symbolId] = info;
            }

            // Update connection counts
            foreach (var connection in symbolUi.Symbol.Connections)
            {
                var hash = connection.SourceSlotId.GetHashCode() * 31
                           + connection.TargetSlotId.GetHashCode();
                ConnectionHashCounts.TryGetValue(hash, out var connectionCount);
                ConnectionHashCounts[hash] = connectionCount + 1;
            }

            usages.TryGetValue(symbolId, out var count);
            info.UsageCount = count;
        }
    }

    /// <summary>
    /// Update <see cref="InformationForSymbolIds"/> collection with details useful for
    /// library structure cleanup, browsing, etc.
    /// </summary>
    internal static void UpdateDetails()
    {
        var usages = CollectSymbolUsageCounts();
        InformationForSymbolIds.Clear();
        TotalUsageCount = 0;

        // Precompute reverse dependencies once for all symbols
        var (_, reverseDeps) = CollectUsageAndReverseDependencies();

        foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
        {
            var symbol = symbolUi.Symbol;
            usages.TryGetValue(symbol.Id, out var usageCount);
            TotalUsageCount += usageCount;

            var requiredSymbols = CollectRequiredSymbols(symbol);
            var invalidRequirements = CollectInvalidRequirements(symbol, requiredSymbols).ToList();
            var invalidAssetReferences = CollectInvalidAssetReferences(symbol, symbolUi);

            reverseDeps.TryGetValue(symbol.Id, out var deps);
            var dependingSymbols = deps ?? new HashSet<Guid>();

            InformationForSymbolIds[symbol.Id] = BuildSymbolInformation(
                                                                        symbol,
                                                                        symbolUi,
                                                                        requiredSymbols,
                                                                        invalidRequirements,
                                                                        invalidAssetReferences,
                                                                        dependingSymbols,
                                                                        usageCount);
        }

        DetailsInitialized = true;
    }

    /// <summary>
    /// Fast single-symbol analysis for dialogs, menus, etc.
    /// Uses cached bulk data when available, otherwise computes on-demand.
    /// </summary>
    /// <param name="symbol">The symbol to analyze.</param>
    /// <param name="info">Returns the collected information for the symbol.</param>
    /// <param name="forceUpdate">
    /// If true, forces a fresh analysis for this symbol even if cached data is available.
    /// If false, uses the cached information when possible.
    /// </param>
    internal static bool TryGetSymbolInfo(Symbol symbol, out SymbolInformation info, bool forceUpdate = false)
    {
        info = new SymbolInformation();

        if (!symbol.TryGetSymbolUi(out var symbolUi))
            return false;

        // If UpdateDetails has run and no force update is requested, return the cached info
        if (!forceUpdate && DetailsInitialized && InformationForSymbolIds.TryGetValue(symbol.Id, out var cached))
        {
            // Keep a copy so callers cannot accidentally mutate the cache.
            info = cached;

            // Keep usage count fresh if we have a newer value
            if (_latestUsageCounts.TryGetValue(symbol.Id, out var usage))
                info.UsageCount = usage;

            return true;
        }

        // On-demand analysis for this single symbol
        var requiredSymbols = CollectRequiredSymbols(symbol);
        var invalidRequirements = CollectInvalidRequirements(symbol, requiredSymbols).ToList();
        var invalidAssetReferences = CollectInvalidAssetReferences(symbol, symbolUi);
        var (usageCounts, reverseDeps) = CollectUsageAndReverseDependencies();

        usageCounts.TryGetValue(symbol.Id, out var usageCount);
        reverseDeps.TryGetValue(symbol.Id, out var deps);
        var dependingSymbols = deps ?? new HashSet<Guid>();

        info = BuildSymbolInformation(
                                      symbol,
                                      symbolUi,
                                      requiredSymbols,
                                      invalidRequirements,
                                      invalidAssetReferences,
                                      dependingSymbols,
                                      usageCount);

        // Optionally refresh the cached entry if details are already initialized
        // so that subsequent calls without forceUpdate can benefit.
        if (DetailsInitialized)
        {
            InformationForSymbolIds[symbol.Id] = info;
        }

        return true;
    }

    /// <summary>
    /// Unified structure with all known symbol metadata.
    /// </summary>
    public sealed class SymbolInformation
    {
        public List<string> Warnings = [];
        internal HashSet<Guid> RequiredSymbolIds = [];
        internal HashSet<Guid> DependingSymbols = [];
        public List<Guid> InvalidRequiredIds = [];
        internal List<AssetReferenceIssue> InvalidAssetReferences = [];
        public IReadOnlyList<Guid> ExampleSymbolsIds = [];
        internal int UsageCount;
        internal bool LacksDescription;
        internal bool LacksAllParameterDescription;
        internal bool LacksSomeParameterDescription;
        internal bool LacksParameterGrouping;
        internal bool DependsOnObsoleteOps;
        internal SymbolUi.SymbolTags Tags; // Copy to avoid reference to symbolUi
        internal OperatorClassification OperatorType;
    }

    /// <summary>
    /// A file or folder input of a child (or a default of the symbol's own input, then <see cref="SymbolChildId"/>
    /// is empty) whose asset would not travel with the symbol's package.
    /// </summary>
    internal readonly record struct AssetReferenceIssue(Guid SymbolChildId, string Address, AssetReferenceProblem Problem, string PackageName);

    internal enum AssetReferenceProblem
    {
        /** The address does not resolve: unknown package or no such file. */
        Missing,

        /** A drive or root path; it only exists on the machine that wrote it. */
        AbsolutePath,

        /** The asset belongs to a package that is neither this one nor shipped with TiXL. */
        OtherPackage,

        /** The address resolves through a linked folder mounted into the package, outside its Assets folder. */
        OutsideAssetsFolder,
    }

    public enum OperatorClassification
    {
        Unknown = 0,
        Lib,
        Type,
        Example,
        T3,
        Skill,
    }

    // Shared helpers (used by both bulk and single analysis)
    #region Shared Helpers
    internal static bool TryGetOperatorType(Symbol symbol, out OperatorClassification opType)
    {
        // Sliced rather than split: the graph calls this for every visible operator each frame.
        var ns = (symbol.Namespace ?? string.Empty).AsSpan();
        var dotIndex = ns.IndexOf('.');
        var rootSegment = dotIndex < 0 ? ns : ns[..dotIndex];

        opType = rootSegment switch
                     {
                         FileLocations.LibPackageName      => OperatorClassification.Lib,
                         FileLocations.TypesPackageName    => OperatorClassification.Type,
                         FileLocations.ExamplesPackageName => OperatorClassification.Example,
                         "t3"                              => OperatorClassification.T3,
                         FileLocations.SkillsPackageName   => OperatorClassification.Skill,
                         _                                 => OperatorClassification.Unknown
                     };
        return opType != OperatorClassification.Unknown;
    }

    private static SymbolInformation BuildSymbolInformation(Symbol symbol,
                                                            SymbolUi symbolUi,
                                                            HashSet<Symbol> requiredSymbols,
                                                            List<Guid> invalidRequirements,
                                                            List<AssetReferenceIssue> invalidAssetReferences,
                                                            HashSet<Guid> dependingSymbols,
                                                            int usageCount)
    {
        var inputUis = symbolUi.InputUis.Values;

        var lacksDescription = string.IsNullOrWhiteSpace(symbolUi.Description);
        var hasManyInputs = symbolUi.InputUis.Count > 2;
        var lacksAllParamDesc = hasManyInputs &&
                                inputUis.All(i => string.IsNullOrWhiteSpace(i.Description));
        var lacksSomeParamDesc = hasManyInputs &&
                                 inputUis.Any(i => string.IsNullOrWhiteSpace(i.Description));

        var lacksParameterGrouping = symbolUi.InputUis.Count > 4 &&
                                     !inputUis.Any(i => i.AddPadding || !string.IsNullOrEmpty(i.GroupTitle));

        var dependsOnObsolete = requiredSymbols
                               .Select(s => s.GetSymbolUi())
                               .Any(ui => ui != null && ui.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete));

        TryGetOperatorType(symbol, out var opType);

        return new SymbolInformation
                   {
                       Warnings = new List<string>(),
                       RequiredSymbolIds = requiredSymbols.Select(s => s.Id).ToHashSet(),
                       DependingSymbols = dependingSymbols,
                       InvalidRequiredIds = invalidRequirements,
                       InvalidAssetReferences = invalidAssetReferences,
                       ExampleSymbolsIds = ExampleSymbolLinking.GetExampleIds(symbol.Id),
                       UsageCount = usageCount,
                       LacksDescription = lacksDescription,
                       LacksAllParameterDescription = lacksAllParamDesc,
                       LacksSomeParameterDescription = lacksSomeParamDesc,
                       LacksParameterGrouping = lacksParameterGrouping,
                       DependsOnObsoleteOps = dependsOnObsolete,
                       Tags = symbolUi.Tags,
                       OperatorType = opType
                   };
    }

    private static HashSet<Symbol> CollectRequiredSymbols(Symbol root)
    {
        var all = new HashSet<Symbol>();
        Collect(root);
        return all;

        void Collect(Symbol symbol)
        {
            foreach (var symbolChild in symbol.Children.Values)
            {
                if (!all.Add(symbolChild.Symbol))
                    continue;
                Collect(symbolChild.Symbol);
            }
        }
    }

    /// <summary>
    /// Collect Ids of required symbols that come neither from the root's own package nor from a package
    /// shipped with TiXL, i.e. operators that would be missing wherever only the root's package is available.
    /// </summary>
    private static IEnumerable<Guid> CollectInvalidRequirements(Symbol root, HashSet<Symbol> requiredSymbols)
    {
        var result = new List<Symbol>();

        foreach (var r in requiredSymbols)
        {
            if (r.SymbolPackage == root.SymbolPackage || IsShippedPackage(r.SymbolPackage))
                continue;

            result.Add(r);
        }

        return result
              .OrderBy(s => s.Namespace)
              .ThenBy(s => s.Name)
              .Select(s => s.Id);
    }

    /// <summary>
    /// True for packages every TiXL installation has: the built-in operator packages except optional ones in a
    /// source build, and in a release every read-only package that wasn't installed as an extension.
    /// </summary>
    private static bool IsShippedPackage(SymbolPackage package)
    {
        if (package is EditableSymbolProject project)
            return project.IsBuiltIn && !OptionalPackagesInSourceBuilds.IsOptional(project.Folder);

        return !InstalledExtensions.ContainsPackageFolder(package.Folder);
    }

    private static List<AssetReferenceIssue> CollectInvalidAssetReferences(Symbol symbol, SymbolUi symbolUi)
    {
        var issues = new List<AssetReferenceIssue>();

        foreach (var inputDefinition in symbol.InputDefinitions)
        {
            if (inputDefinition.DefaultValue is not InputValue<string> defaultValue)
                continue;

            if (symbolUi.InputUis.TryGetValue(inputDefinition.Id, out var inputUi) && inputUi is StringInputUi stringUi)
                CheckAssetAddress(symbol.SymbolPackage, Guid.Empty, defaultValue.Value, stringUi.Usage, issues);
        }

        foreach (var child in symbol.Children.Values)
        {
            if (!SymbolUiRegistry.TryGetSymbolUi(child.Symbol.Id, out var childSymbolUi))
                continue;

            foreach (var input in child.Inputs.Values)
            {
                if (input.IsDefault || input.Value is not InputValue<string> stringValue)
                    continue;

                if (childSymbolUi.InputUis.TryGetValue(input.Id, out var inputUi) && inputUi is StringInputUi stringUi)
                    CheckAssetAddress(symbol.SymbolPackage, child.Id, stringValue.Value, stringUi.Usage, issues);
            }
        }

        return issues;
    }

    private static void CheckAssetAddress(SymbolPackage owner, Guid symbolChildId, string? address, StringInputUi.UsageType usage,
                                          List<AssetReferenceIssue> issues)
    {
        // Output paths are written by renders and exports and need not exist yet
        if (usage is not (StringInputUi.UsageType.FilePath or StringInputUi.UsageType.DirectoryPath))
            return;

        if (string.IsNullOrWhiteSpace(address) || address.Contains("://", StringComparison.Ordinal))
            return;

        var isWindowsRooted = address.Length > 1 && address[1] == ':';
        if (isWindowsRooted || address[0] == '/' || address[0] == '\\')
        {
            issues.Add(new AssetReferenceIssue(symbolChildId, address, AssetReferenceProblem.AbsolutePath, string.Empty));
            return;
        }

        var isFolder = usage == StringInputUi.UsageType.DirectoryPath;
        if (!AssetRegistry.TryResolveAddress(address, null, out var absolutePath, out var resourcePackage, isFolder))
        {
            var separatorIndex = address.IndexOf(':');
            var packageName = separatorIndex > 0 ? address[..separatorIndex] : string.Empty;
            issues.Add(new AssetReferenceIssue(symbolChildId, address, AssetReferenceProblem.Missing, packageName));
            return;
        }

        if (resourcePackage is not SymbolPackage assetPackage)
            return;

        if (assetPackage == owner)
        {
            var assetsFolder = Path.GetFullPath(owner.AssetsFolder) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(absolutePath).StartsWith(assetsFolder, StringComparison.OrdinalIgnoreCase))
                issues.Add(new AssetReferenceIssue(symbolChildId, address, AssetReferenceProblem.OutsideAssetsFolder, owner.Name));

            return;
        }

        if (!IsShippedPackage(assetPackage))
            issues.Add(new AssetReferenceIssue(symbolChildId, address, AssetReferenceProblem.OtherPackage, assetPackage.Name));
    }

    private static Dictionary<Guid, int> CollectSymbolUsageCounts()
    {
        var results = new Dictionary<Guid, int>();
        TotalUsageCount = 0;

        foreach (var s in EditorSymbolPackage.AllSymbols)
        {
            foreach (var child in s.Children.Values)
            {
                results.TryGetValue(child.Symbol.Id, out var currentCount);
                results[child.Symbol.Id] = currentCount + 1;
                TotalUsageCount++;
            }
        }

        _latestUsageCounts.Clear();
        foreach (var kvp in results)
            _latestUsageCounts[kvp.Key] = kvp.Value;

        return results;
    }

    /// <summary>
    /// Builds per-symbol usage counts and a reverse dependency map in a single pass
    /// over <see cref="EditorSymbolPackage.AllSymbols"/>.
    /// This is used by single-symbol analysis without running the full detail update.
    /// </summary>
    private static (Dictionary<Guid, int> UsageCounts, Dictionary<Guid, HashSet<Guid>> ReverseDependencies)
        CollectUsageAndReverseDependencies()
    {
        var usageCounts = new Dictionary<Guid, int>();
        var reverseDeps = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var container in EditorSymbolPackage.AllSymbols)
        {
            var containerId = container.Id;
            foreach (var child in container.Children.Values)
            {
                var childId = child.Symbol.Id;
                usageCounts.TryGetValue(childId, out var current);
                usageCounts[childId] = current + 1;

                if (!reverseDeps.TryGetValue(childId, out var set))
                {
                    set = new HashSet<Guid>();
                    reverseDeps[childId] = set;
                }

                set.Add(containerId);
            }
        }

        return (usageCounts, reverseDeps);
    }
    #endregion

    public static bool TryGetFileInputFromInstance(Instance instance,
                                                   [NotNullWhen(true)] out InputSlot<string>? stringInput,
                                                   [NotNullWhen(true)] out StringInputUi? stringInputUi)
    {
        stringInput = null;
        stringInputUi = null;

        var symbolUi = instance.GetSymbolUi();
        foreach (var input in instance.Inputs)
        {
            if (input is not InputSlot<string> tmpStringInput)
                continue;

            stringInput = tmpStringInput;

            var inputUi = symbolUi.InputUis[input.Id];
            if (inputUi is not StringInputUi { Usage: StringInputUi.UsageType.FilePath } tmpStringInputUi)
                continue;

            stringInputUi = tmpStringInputUi;
            return true;
        }

        return false;
    }
}