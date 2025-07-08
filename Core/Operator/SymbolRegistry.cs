#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using T3.Core.Model;

namespace T3.Core.Operator;

public static class SymbolRegistry //: IDisposable
{
    public static bool TryGetSymbol(Guid symbolId, [NotNullWhen(true)] out Symbol? symbol)
    {
        foreach(var package in SymbolPackage.AllPackages)
        {
            if (package.Symbols.TryGetValue(symbolId, out symbol))
                return true;
        }
        symbol = null;
        return false;
    }
}