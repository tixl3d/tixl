#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using T3.Core.Operator;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

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

    /// <summary>
    /// Basic usage info used by symbol browser and relevancy search.
    /// </summary>
    internal static void UpdateSymbolUsageCounts()
    {
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

            reverseDeps.TryGetValue(symbol.Id, out var deps);
            var dependingSymbols = deps ?? new HashSet<Guid>();

            InformationForSymbolIds[symbol.Id] = BuildSymbolInformation(
                symbol,
                symbolUi,
                requiredSymbols,
                invalidRequirements,
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
        var (usageCounts, reverseDeps) = CollectUsageAndReverseDependencies();

        usageCounts.TryGetValue(symbol.Id, out var usageCount);
        reverseDeps.TryGetValue(symbol.Id, out var deps);
        var dependingSymbols = deps ?? new HashSet<Guid>();

        info = BuildSymbolInformation(
            symbol,
            symbolUi,
            requiredSymbols,
            invalidRequirements,
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
        var ns = symbol.Namespace ?? string.Empty;
        var rootSegment = ns.Split('.')[0];

        opType = rootSegment switch
                     {
                         "Lib"      => OperatorClassification.Lib,
                         "Types"    => OperatorClassification.Type,
                         "Examples" => OperatorClassification.Example,
                         "t3"       => OperatorClassification.T3,
                         "Skills"   => OperatorClassification.Skill,
                         _          => OperatorClassification.Unknown
                     };
        return opType != OperatorClassification.Unknown;
    }

    private static SymbolInformation BuildSymbolInformation(
        Symbol symbol,
        SymbolUi symbolUi,
        HashSet<Symbol> requiredSymbols,
        List<Guid> invalidRequirements,
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
    /// Collect Ids of required symbols that are not within the list of projects
    /// </summary>
    private static IEnumerable<Guid> CollectInvalidRequirements(Symbol root, HashSet<Symbol> requiredSymbols)
    {
        var result = new List<Symbol>();

        // Todo: implement this correctly?
        HashSet<string> validPackagesNames = new()
        {
            "Types",
            "Lib",
            root.SymbolPackage.RootNamespace,
        };

        foreach (var r in requiredSymbols)
        {
            var projectId = r.SymbolPackage.RootNamespace;
            if (validPackagesNames.Contains(projectId))
                continue;

            result.Add(r);
        }

        return result
            .OrderBy(s => s.Namespace)
            .ThenBy(s => s.Name)
            .Select(s => s.Id);
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
}
