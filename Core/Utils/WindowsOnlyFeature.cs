using System;
using System.Collections.Generic;
using T3.Core.Logging;

namespace T3.Core.Utils;

/// <summary>
/// Guards operators that need GDI+ (<c>System.Drawing</c>) or the SVG library built on it. Those assemblies only
/// exist on Windows, and an operator that touches them elsewhere fails while its method is being prepared —
/// which no <c>try</c> inside that method can catch, so the editor dies with it.
/// </summary>
/// <remarks>
/// The guarded work has to live in a separate method: everything a method mentions is resolved before its first
/// line runs, so a check in the same method comes too late.
/// </remarks>
public static class WindowsOnlyFeature
{
    /// <summary>True where the feature works. Elsewhere it says so once and the operator leaves its output empty.</summary>
    public static bool IsAvailable(string feature)
    {
        if (OperatingSystem.IsWindows())
            return true;

        if (_reported.Add(feature))
            Log.Warning($"{feature} is only available on Windows.");

        return false;
    }

    private static readonly HashSet<string> _reported = [];
}
