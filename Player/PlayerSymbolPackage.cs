using T3.Core.Compilation;
using T3.Core.Model;

namespace T3.Player;

internal sealed class PlayerSymbolPackage(AssemblyInformation assembly) : SymbolPackage(assembly, null, true);