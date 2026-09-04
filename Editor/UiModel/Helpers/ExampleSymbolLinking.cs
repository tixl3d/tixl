using T3.Core.Operator;

namespace T3.Editor.UiModel.Helpers;

/// <summary>
/// Provides <see cref="ExampleIdsForSymbolsId"/> collection of examples operators for each symbol.
/// </summary>
/// <remarks>
/// Should be updated on editor startup. The only stores symbolIds to prevent references to
/// outdated types after structural changes.
/// </remarks>
/// <todo>
/// Should be "occasionally" structural changes like creating or renaming symbols.
/// </todo>
internal static class ExampleSymbolLinking
{
    internal static Dictionary<Guid, List<Guid>> ExampleIdsForSymbolsId { get; } = new(50);

    public static IReadOnlyList<Guid> GetExampleIds(Guid id)
    {
        return ExampleIdsForSymbolsId.TryGetValue(id, out var exampleIds) ? exampleIds : _emptyIdList;
    } 
    
    public static bool TryGetExamples(Guid symbolId, out IReadOnlyList<SymbolUi> examples)
    {
        examples = _noSymbolUis;
        if (!ExampleIdsForSymbolsId.TryGetValue(symbolId, out var exampleIds))
            return false;

        if (exampleIds.Count == 0)
            return false;

        var newList = new List<SymbolUi>(examples.Count);
        foreach (var id in exampleIds)
        {
            if (SymbolUiRegistry.TryGetSymbolUi(id, out var symbolUi))
                 newList.Add(symbolUi);
        }

        examples = newList;
        return true;
    }
    
    
    // Todo: unlink Examples implementation from naming - this should be done in a different way via attribute, guid links, etc.
    internal static void UpdateExampleLinks()
    {
        ExampleIdsForSymbolsId.Clear();

        const string exampleSuffix = "Example";
        const string examplesSuffix = "Examples";
        
        var potentialExamples = new List<(Range correspondingNameRange, SymbolUi exampleSymbol)>();

        var symbolUiCollection = EditorSymbolPackage.AllSymbolUis.ToList();
        
        Dictionary<int, List<Symbol>> symbolsByName = new(capacity: symbolUiCollection.Count);
        foreach (var symbolUi in symbolUiCollection)
        {
            var potentialExampleSymbol = symbolUi.Symbol;
            var name = potentialExampleSymbol.Name;
            var hashCode = name.GetHashCode();

            if (!symbolsByName.TryGetValue(hashCode, out var symbolsWithName))
            {
                symbolsWithName = [];
                symbolsByName.Add(hashCode, symbolsWithName);
            }
                
            symbolsWithName.Add(potentialExampleSymbol);

            var lastChar = name[^1];
            var endsWithS = lastChar == 's';
            var endsWithE = lastChar == 'e';
            if(!endsWithS && !endsWithE)
                continue;

            var index = endsWithS 
                            ? name.IndexOf(examplesSuffix, StringComparison.Ordinal) 
                            : name.IndexOf(exampleSuffix, StringComparison.Ordinal);
            if (index < 0)
                continue;

            potentialExamples.Add((..index, symbolUi));
        }

        foreach (var (correspondingSymbolNameRange, exampleSymbol) in potentialExamples)
        {
            var name = exampleSymbol.Symbol.Name[correspondingSymbolNameRange];
            var gotSymbol = symbolsByName.TryGetValue(name.GetHashCode(), out var symbolsWithName);

            if (!gotSymbol)
                continue;

            foreach (var symbol in symbolsWithName)
            {
                if (!ExampleIdsForSymbolsId.TryGetValue(symbol.Id, out var exampleList))
                {
                    exampleList = [];
                    ExampleIdsForSymbolsId.Add(symbol.Id, exampleList);
                }

                exampleList.Add(exampleSymbol.Symbol.Id);
            }
        }

        Log.Debug($"Found {ExampleIdsForSymbolsId.Sum(x=> x.Value.Count)} examples for {ExampleIdsForSymbolsId.Count} operators out of {potentialExamples.Count} potential examples");
    }

    private static readonly List<Guid> _emptyIdList = [];
    private static readonly IReadOnlyList <SymbolUi> _noSymbolUis = []; 
}