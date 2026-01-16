using System.Runtime.CompilerServices;
using T3.Core.Resource;
using T3.Core.Utils;

namespace T3.Core.Operator;

public abstract partial class Instance
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetFilePath(string relativePath, out string absolutePath, bool isFolder = false)
    {
        return ResourceManager.TryResolveRelativePath(relativePath, this, out absolutePath, out _, isFolder);
    }

    // note: it could be nice to make this an abstract method, but that would require all existing user ops to
    // implement it, which would be a pain in the arse to do automagically atm
    protected virtual void Dispose(bool disposing)
    {
    }

    public sealed override string ToString()
    {
        const string fmt = "{0} ({1})";
        return _asString ??= string.Format(fmt, GetType().Name, SymbolChildId.ShortenGuid());
    }
}