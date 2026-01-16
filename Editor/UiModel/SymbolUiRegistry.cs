#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Model;
using T3.Core.Operator;

namespace T3.Editor.UiModel;

internal static class SymbolUiRegistry
{
    public static SymbolUi GetSymbolUi(this Instance instance) => instance.Symbol.GetSymbolUi();


    public static bool TryGetSymbolUi(this Symbol symbol, [NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        var id = symbol.Id;
        var package = (EditorSymbolPackage)symbol.SymbolPackage;
        return package.TryGetSymbolUi(id, out symbolUi);
    }


    public static SymbolUi GetSymbolUi(this Symbol symbol)
    {
        var id = symbol.Id;
        var package = (EditorSymbolPackage)symbol.SymbolPackage;
        if (package.TryGetSymbolUi(id, out var symbolUi))
            return symbolUi!;

        throw new Exception($"Can't find symbol ui for symbol {id}");
    }
    
    public static bool TryGetSymbolUi(Guid symbolId, [NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        foreach(var package in SymbolPackage.AllPackages)
        {
            if (package is not EditorSymbolPackage symbolPackage)
                continue;
            
            if (symbolPackage.TryGetSymbolUi(symbolId, out symbolUi))
                return true;
        }
        symbolUi = null;
        return false;
    }
    
    public static SymbolUi.Child? GetChildUi(this Instance instance) => instance.SymbolChild?.GetChildUi();
    public static SymbolUi.Child GetChildUi(this Symbol.Child symbolChild) => symbolChild.Parent!.GetSymbolUi().ChildUis[symbolChild.Id];
}