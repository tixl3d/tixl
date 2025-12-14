#nullable enable
using System.Diagnostics;
using System.Text.RegularExpressions;
using T3.Core.Operator;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel.Helpers;

/// <summary>
/// Provides a regular expression to filter and sort matching <see cref="Symbol"/>s
/// </summary>
internal sealed class SymbolFilter
{
    public string SearchString = string.Empty; // not a property to allow ref passing

    public Type? FilterInputType
    {
        get => _inputType;
        set
        {
            _needsUpdate = true;
            _inputType = value;
        }
    }

    public Type? FilterOutputType
    {
        get => _outputType;
        set
        {
            _needsUpdate = true;
            _outputType = value;
        }
    }

    public void Reset()
    {
        _inputType = null;
        _outputType = null;
        SearchString = string.Empty;
        PresetFilterString = string.Empty;
        OnlyMultiInputs = false;
        _needsUpdate = true;
    }

    public bool OnlyMultiInputs { get; set; }
    public List<SymbolUi> MatchingSymbolUis { get; private set; } = [];

    public void UpdateIfNecessary(NodeSelection? selection, bool forceUpdate = false, int limit = 0)
    {
        _needsUpdate |= forceUpdate;
        _needsUpdate |= UpdateFilters(SearchString,
                                      ref _lastSearchString,
                                      ref _symbolFilterString,
                                      ref PresetFilterString,
                                      ref _currentRegex);

        if (_needsUpdate)
        {
            //UpdateConnectSlotHashes();  //TODO: Clarify why this is commented out
            UpdateMatchingSymbols(selection, limit);
        }

        WasUpdated = _needsUpdate;
        _needsUpdate = false;
    }

    private static bool UpdateFilters(string search,
                                      ref string lastSearch, ref string symbolFilter, ref string presetFilter, ref Regex searchRegex)
    {
        if (search == lastSearch)
            return false;

        lastSearch = search;

        // Check if template search was initiated 
        var twoPartSearchResult = new Regex(@"(.+?)\s+(.*)").Match(search);
        if (twoPartSearchResult.Success)
        {
            symbolFilter = twoPartSearchResult.Groups[1].Value;
            presetFilter = twoPartSearchResult.Groups[2].Value;
        }
        else
        {
            symbolFilter = search;
            presetFilter = string.Empty;
        }

        var pattern = string.Join(".*", symbolFilter.ToCharArray());
        try
        {
            searchRegex = new Regex(pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            Log.Debug("Invalid Regex format: " + pattern);
            return true;
        }

        return true;
    }

    private void UpdateMatchingSymbols(NodeSelection? selection, int limit)
    {
        var compositionInstance = selection?.GetSelectedComposition();
        ICollection<Guid> parentSymbolIds = compositionInstance != null
                                                ? new HashSet<Guid>(Structure.CollectParentInstances(compositionInstance).Append(compositionInstance)
                                                                             .Select(p => p.Symbol.Id))
                                                : Array.Empty<Guid>();

        MatchingSymbolUis.Clear();
        foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
        {
            var symbolUiSymbol = symbolUi.Symbol;
            Debug.Assert(symbolUiSymbol != null);

            // Prevent graph cycles
            if (parentSymbolIds.Contains(symbolUiSymbol.Id))
                continue;

            if (_inputType != null)
            {
                 if (symbolUiSymbol.InputDefinitions.Count == 0 || symbolUiSymbol.InputDefinitions[0].ValueType != _inputType)
                     continue;

                 var matchingInputDef = symbolUiSymbol.GetInputMatchingType(FilterInputType);
                
                 if (matchingInputDef == null)
                     continue;

                 if (OnlyMultiInputs && !symbolUiSymbol.InputDefinitions[0].IsMultiInput)
                     continue;
            }

            if (_outputType != null)
            {
                var matchingOutputDef = symbolUiSymbol.GetOutputMatchingType(FilterOutputType);
                
                if (matchingOutputDef == null)
                    continue;
            }

            if (!(_currentRegex.IsMatch(symbolUiSymbol.Name)
                  || symbolUiSymbol.Namespace.Contains(_symbolFilterString, StringComparison.InvariantCultureIgnoreCase)
                  || (!string.IsNullOrEmpty(symbolUi.Description)
                      && symbolUi.Description.Contains(_symbolFilterString, StringComparison.InvariantCultureIgnoreCase))))
                continue;

            MatchingSymbolUis.Add(symbolUi);
        }

        EditorSymbolPackage? currentProject = null;
        Instance? composition = null;

        if (ProjectView.Focused != null)
        {
            currentProject = ProjectView.Focused.OpenedProject.Package;
            composition = ProjectView.Focused.CompositionInstance;
        }

        if (limit == 0)
        {
            MatchingSymbolUis = MatchingSymbolUis.OrderBy(s => ComputeRelevancy(s, _symbolFilterString, currentProject, composition))
                                                 .Reverse()
                                                 .Where(s => ComputeRelevancy(s, _symbolFilterString, currentProject, composition) >= 10f) // Ensure there is some relevancy to the results
                                                 .ToList();
        }
        else
        {
            MatchingSymbolUis = MatchingSymbolUis.OrderBy(s => ComputeRelevancy(s, _symbolFilterString, currentProject, composition))
                                                 .Reverse()
                                                 .Take(limit)
                                                 .Where(s => ComputeRelevancy(s, _symbolFilterString, currentProject, composition) >= 10f) // Ensure there is some relevancy to the results
                                                 .ToList();
        }


        // Debug log to help tweaking relevancy factors
        // foreach (var s in MatchingSymbolUis)
        // {
        //     ComputeRelevancy(s, _symbolFilterString, currentProject, composition, logOutput:true);
        // }
    }

    private static readonly List<string> _logList = [];

    internal static double ComputeRelevancy(SymbolUi symbolUi,
                                            string query,
                                            EditorSymbolPackage? currentProject,
                                            Instance? composition,
                                            int targetInputHash = 0,
                                            Type? filterInputType = null,
                                            Type? filterOutputType = null,
                                            bool logOutput = false)
    {
        float relevancy = 1;

        var symbol = symbolUi.Symbol;
        var symbolName = symbol.Name;
        _logList.Clear();
        _logList.Add(symbolUi.Symbol.Name);

        if (symbol.Namespace.StartsWith("Types.", StringComparison.InvariantCulture))
        {
            _logList.Add("Type: x4");
            relevancy *= 4;
        }

        if (symbolName.Equals(query, StringComparison.InvariantCultureIgnoreCase))
        {
            _logList.Add("Equals: x8.6");
            relevancy *= 8.6f;
        }

        if (symbolName.StartsWith(query, StringComparison.InvariantCultureIgnoreCase))
        {
            _logList.Add("StartsWith: x8.5");
            relevancy *= 8.5f;
        }
        else
        {
            if (symbolName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logList.Add("Contains: x8.4");
                relevancy *= 8.4f;
            }
        }

        if (!string.IsNullOrEmpty(symbolUi.Description)
            && symbolUi.Description.Contains(query, StringComparison.InvariantCultureIgnoreCase))
        {
            _logList.Add("Description: x1.01");
            relevancy *= 1.01f;
        }

        
        // Bump if characters match upper characters
        // e.g. "ds" matches "DrawState"
        {
            var pascalCaseMatch = true;
            
            var uppercaseQuery = query.ToUpper();
            var maxIndex = 0;

            foreach (var indexInName in uppercaseQuery.Select(c => symbolName.IndexOf(c, maxIndex)))
            {
                if (indexInName == -1)
                {
                    pascalCaseMatch = false;
                    break;
                }

                maxIndex = indexInName + 1; // Move past the current character for the next search
            }

            if (pascalCaseMatch)
            {
                _logList.Add($"PascalMatch: x4");
                relevancy *= 4f;
            }
        }

        if (!string.IsNullOrEmpty(symbol.Namespace))
        {
            if (symbol.Namespace.Contains("dx11")
                || symbol.Namespace.Contains("_"))
                relevancy *= 0.1f;

            if (symbol.Namespace.StartsWith("Lib"))
                relevancy *= 3f;

            if (symbol.Namespace.StartsWith("examples"))
            {
                _logList.Add($"Examples: x2");
                relevancy *= 2f;
            }
        }

        if (symbolName.StartsWith("_"))
        {
            _logList.Add($"_sym x0.1");
            relevancy *= 0.1f;
        }

        if (symbolName.Contains("OBSOLETE"))
            relevancy *= 0.01f;

        var symbolId = symbol.Id;
        var symbolPackage = symbol.SymbolPackage;
        if (currentProject != null)
        {
            // mega-boost symbols from the same package as the current project
            if (currentProject == symbolPackage)
            {
                _logList.Add($"Project: x2");
                relevancy *= 2f;
            }

            // or boost symbols from related namespaces
            else if (symbol.Namespace!.StartsWith(currentProject.RootNamespace))
            {
                _logList.Add($"RootName: x1.9");
                relevancy *= 1.9f;
            }
        }

        if (composition != null)
        {
            var compositionSymbol = composition.Symbol;
            var compositionPackage = compositionSymbol.SymbolPackage;

            // boost symbols from the same package as composition, or from related namespaces
            if (compositionPackage.Symbols.ContainsKey(symbolId) || symbolPackage.RootNamespace.StartsWith(compositionPackage.RootNamespace))
            {
                _logList.Add($"Package: x1.9");
                relevancy *= 1.9f;
            }
        }

        // boost user symbols
        if (symbolPackage is EditableSymbolProject)
        {
            _logList.Add($"Editable: x1.9");
            relevancy *= 1.9f;
        }

        // Bump operators with matching input connections 
        var matchingInputConnectionsCount = 0;
        if (targetInputHash != 0)
        {
            foreach (var outputDefinition in symbol.OutputDefinitions.FindAll(o => o.ValueType == filterOutputType))
            {
                var connectionHash = outputDefinition.Id.GetHashCode() * 31 + targetInputHash;

                if (SymbolAnalysis.ConnectionHashCounts.TryGetValue(connectionHash, out var connectionCount))
                {
                    matchingInputConnectionsCount += connectionCount;
                }
            }
        }

        if (matchingInputConnectionsCount > 0)
        {
            var matchingInputsBoost = 1 + MathF.Pow(matchingInputConnectionsCount, 0.33f) * 4f;
            _logList.Add($"matchingInputs: x{matchingInputsBoost}");
            relevancy *= matchingInputsBoost;
        }

        // If the symbol is at least slightly relevant after all checks, we boost it by usage amount
        if (relevancy > 10f)
        {
            // Add usage count (the following statement is slow and should be cached)
            var count = SymbolAnalysis.InformationForSymbolIds.TryGetValue(symbol.Id, out var info)
                            ? info.UsageCount
                            : 0;

            //symbolUi.Symbol.InstancesOfSymbol.Select(instance =>instance.SymbolChildId).Distinct().Count();
            
            var totalUsageCountBoost = (float)(1 + (500.0 * (float)count / SymbolAnalysis.TotalUsageCount));
            if (count > 0)
            {
                _logList.Add($"Used {count}: {totalUsageCountBoost:0.0}");
            }
            relevancy *= totalUsageCountBoost;
        }

        if (logOutput)
        {
            Log.Debug( $"{relevancy:0.0} " + string.Join(", ",_logList));
        }
        return relevancy;
    }

    private bool _needsUpdate;
    private string _symbolFilterString = string.Empty;
    public string PresetFilterString = string.Empty;

    private Type? _inputType;
    private Type? _outputType;
    public bool WasUpdated;

    // TODO:  implement for relevancy filter for matching outputs
    private const int SourceInputHash = 0;

    //private int _targetInputHash =0;

    private Regex _currentRegex = new(".*", RegexOptions.IgnoreCase);
    private string _lastSearchString = string.Empty;
}