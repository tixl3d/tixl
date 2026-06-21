#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using T3.Core.Logging;

namespace T3.Core.Diagnostics;

/// <summary>
/// Registers a hook on <see cref="AppDomain.AssemblyResolve"/> that logs the missing
/// assembly name and its requester before the CLR throws <see cref="System.IO.FileNotFoundException"/>.
/// The handler returns <c>null</c> — it does not attempt to recover, only to enrich the
/// log trail. Captured by Sentry breadcrumbs, so the next crash report carries the context.
/// Fires only on resolution failure; zero cost on the happy path.
/// </summary>
public static class AssemblyLoadDiagnostics
{
    private static bool _installed;
    private static readonly object _gate = new();

    private static readonly HashSet<string> _alreadyLogged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Idempotent. Call as early as possible in <c>Main</c>.</summary>
    public static void Install()
    {
        lock (_gate)
        {
            if (_installed)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _installed = true;
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var assemblyName = args.Name ?? "<unknown>";

        // Satellite resource assemblies (Culture=en-US etc.) aren't always deployed;
        // the runtime falls back to the neutral culture transparently. Not an error.
        try
        {
            var parsedName = new AssemblyName(assemblyName);
            if (!string.IsNullOrEmpty(parsedName.CultureName))
                return null;

            // Pre-generated XmlSerializer assemblies (e.g. 'Lib.XmlSerializers') are an
            // opt-in build artifact we don't produce; the runtime emits the serializer at
            // runtime instead. The probe always fails here — expected, not a broken install.
            if (parsedName.Name?.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase) == true)
                return null;
        }
        catch { /* malformed name — fall through to log */ }

        var requester = args.RequestingAssembly?.GetName().Name ?? "<unknown>";

        // Log each missing assembly once per session.
        lock (_gate)
        {
            if (!_alreadyLogged.Add(assemblyName))
                return null;
        }

        Log.Warning(
            $"AssemblyResolve failed: '{assemblyName}' (requested by '{requester}'). " +
            "This usually indicates a missing or corrupt TiXL install file — " +
            "antivirus quarantine, partial install, or manual deletion are the common causes. " +
            "Reinstalling TiXL is recommended.");

        return null;
    }
}
