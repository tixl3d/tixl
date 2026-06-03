#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace T3.Core.Resource;

/// <summary>
/// Runtime components — often native libraries loaded by operator packages in a separate load context —
/// register a short version/license line here so the editor can surface it (e.g. the About dialog, for
/// license transparency). Lives in Core because Core is the assembly shared across the editor and the
/// operator load contexts, so a registration from an operator package is visible to the editor.
/// </summary>
public static class ThirdPartyRuntimeInfo
{
    /// <summary>Registers or replaces the line for <paramref name="name"/> (e.g. "FFmpeg" → "7.0 (LGPL)").</summary>
    public static void Register(string name, string details)
    {
        lock (_lock)
            _entries[name] = details;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> GetAll()
    {
        lock (_lock)
            return _entries.ToList();
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _entries = new();
}
