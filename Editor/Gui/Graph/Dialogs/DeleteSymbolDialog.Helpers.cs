#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Dialogs;

internal sealed partial class DeleteSymbolDialog
{
    /// <summary>
    /// Attempts to retrieve a restriction reason for the given symbol, such as library membership or read-only status.
    /// Returns <see langword="true"/> and sets <paramref name="restriction"/> to a non-null description if restricted;
    /// otherwise returns <see langword="false"/> and sets <paramref name="restriction"/> to <see langword="null"/>.
    /// </summary>
    /// <param name="symbol">The symbol to check for restrictions.</param>
    /// <param name="info">Symbol information containing library operator flags.</param>
    /// <param name="restriction">
    /// When this method returns <see langword="true"/>, contains the restriction reason (non-null).
    /// When <see langword="false"/>, set to <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the symbol has a restriction; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryGetRestriction(Symbol symbol, LocalSymbolInfo info, [NotNullWhen(true)] out string? restriction)
    {
#if DEBUG
        // In debug mode, always allow deletion but show restriction info and debug mode status
        var restrictionReason = info.OperatorType switch
        {
            SymbolAnalysis.OperatorClassification.Lib     => "part of the Main Library",
            SymbolAnalysis.OperatorClassification.Type    => "part of the Types Library",
            SymbolAnalysis.OperatorClassification.Example => "part of the Examples Library",
            SymbolAnalysis.OperatorClassification.T3      => "part of the T3 Library",
            SymbolAnalysis.OperatorClassification.Skill   => "part of the Skills Library",
            _ when IsNamespaceMainSymbol(symbol)          => "the main symbol of this namespace and attached to the project",
            _ when symbol.SymbolPackage.IsReadOnly        => "Read Only",
            _                                             => null
        };

        if (restrictionReason != null)
        {
            restriction = $"[DEBUG MODE] Would restrict deletion: {restrictionReason}";
        }
        else
        {
            restriction = "[DEBUG MODE] No restrictions apply.";
        }
        return false; // Never restrict in debug mode
#else
        restriction = info.OperatorType switch
        {
            SymbolAnalysis.OperatorClassification.Lib     => "part of the Main Library",
            SymbolAnalysis.OperatorClassification.Type    => "part of the Types Library",
            SymbolAnalysis.OperatorClassification.Example => "part of the Examples Library",
            SymbolAnalysis.OperatorClassification.T3      => "part of the T3 Library",
            SymbolAnalysis.OperatorClassification.Skill   => "part of the Skills Library",
            _ when IsNamespaceMainSymbol(symbol)          => "the main symbol of this namespace and attached to the project",
            _ when symbol.SymbolPackage.IsReadOnly        => "Read Only",
            _                                             => null
        };

        return restriction != null;
#endif
    }
    
    /// <summary>
    /// Checks if the symbol acts as the main symbol for its namespace, which binds it
    /// to the project namespace and prevents regular deletion.
    /// </summary>
    /// <param name="symbol">The symbol to inspect.</param>
    /// <returns>
    /// <c>true</c> if the symbol name matches the last segment of its namespace;
    /// otherwise, <c>false</c>.
    /// </returns>
    private static bool IsNamespaceMainSymbol(Symbol symbol)
    {
        var ns = symbol.Namespace ?? string.Empty;
        if (string.IsNullOrEmpty(ns))
            return false;

        var lastDotIndex = ns.LastIndexOf('.');
        var lastSegment  = lastDotIndex >= 0 ? ns[(lastDotIndex + 1)..] : ns;
        return string.Equals(lastSegment, symbol.Name, StringComparison.Ordinal);
    }

    // Deletion helpers
    #region Symbol Deletion Helpers
    /// <summary>
    /// Unified symbol deletion: optionally cleans all usages in depending symbols,
    /// deletes the symbol files on disk, and triggers project reload/recompile for affected projects.
    /// </summary>
    /// <param name="symbol">The symbol to delete.</param>
    /// <param name="dependingSymbols">
    /// Optional set of symbol IDs that reference <paramref name="symbol"/> and should be cleaned up
    /// before deletion, or <c>null</c> if there are no known usages.
    /// </param>
    /// <param name="reason">
    /// On failure, receives an explanation why deletion did not complete.
    /// Empty if the operation succeeded.
    /// </param>
    /// <returns><c>true</c> if the symbol and its usages were deleted successfully; otherwise, <c>false</c>.</returns>
    private static bool DeleteSymbol(Symbol symbol, HashSet<Guid>? dependingSymbols, out string reason)
    {
        if (dependingSymbols is { Count: > 0 })
        {
            if (!CleanUsages(symbol, dependingSymbols, out reason))
                return false;
        }

        if (!DeleteSymbolFiles(symbol, out reason))
            return false;

        ForceReloadSymbolLibrary(dependingSymbols);
        return true;
    }

    /// <summary>
    /// Removes all physical files that define the given symbol (code, graph, and UI files),
    /// and clears build artifacts (<c>bin</c>/<c>obj</c>) for the containing project.
    /// </summary>
    /// <param name="symbol">The symbol whose backing files should be removed.</param>
    /// <param name="reason">
    /// On failure, receives a detailed error message describing what went wrong;
    /// empty if the deletion completed successfully.
    /// </param>
    /// <returns><c>true</c> if all relevant files and directories were deleted; otherwise, <c>false</c>.</returns>
    private static bool DeleteSymbolFiles(Symbol symbol, out string reason)
    {
        if (!symbol.TryGetSymbolUi(out var symbolUi))
        {
            reason = $"Could not find SymbolUi for symbol '{symbol.Name}'";
            return false;
        }

        if (symbolUi.Symbol.SymbolPackage.IsReadOnly)
        {
            reason = $"Could not delete [{symbol.Name}] because its package is not modifiable";
            return false;
        }

        try
        {
            // Build folder structure from RootNamespace and Symbol.Namespace
            var rootNs = symbol.SymbolPackage.RootNamespace;
            var symbolNs = symbol.Namespace;

            var project = (EditableSymbolProject)symbol.SymbolPackage;
            var csPath = SymbolPathHandler.GetCorrectSourceCodePath(symbol.Name, symbolNs, project);
            var t3Path = SymbolPathHandler.GetCorrectPath(symbol.Name, symbolNs, project.Folder, project.CsProjectFile.RootNamespace, SymbolPackage.SymbolExtension);
            var t3UiPath = SymbolPathHandler.GetCorrectPath(symbol.Name, symbolNs, project.Folder, project.CsProjectFile.RootNamespace, EditorSymbolPackage.SymbolUiExtension);
            
            var paths = new[] { csPath, t3Path, t3UiPath };
            
            if (!File.Exists(csPath))
            {
                reason = $"""
                         Could not locate the source file for symbol [{symbol.Name}]
                         RootNamespace: [{rootNs}]
                         Namespace: [{symbolNs}]
                         Expected at: {csPath}
                         """;
                return false;
            }

            var projectRoot = Path.GetDirectoryName(symbol.SymbolPackage.Folder);
            if (string.IsNullOrEmpty(projectRoot))
            {
                reason = $"Could not determine the directory for symbol [{symbol.Name}]";
                return false;
            }
            
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                File.Delete(path);
                Log.Debug($"Deleted: '{path}'");
            }

            reason = string.Empty;
            return true;
        }
        catch (IOException ioEx)
        {
            reason = $"I/O error while deleting symbol [{symbol.Name}]: {ioEx.Message}";
            return false;
        }
        catch (UnauthorizedAccessException authEx)
        {
            reason = $"Access denied while deleting symbol [{symbol.Name}]: {authEx.Message}";
            return false;
        }
        catch (Exception ex)
        {
            reason = $"Unexpected error while deleting symbol [{symbol.Name}]: {ex.Message}";
            return false;
        }
    }
    
    ///  <summary>
    /// Disconnects and removes all child instances in depending symbols that reference
    /// the symbol being deleted, without touching runtime instances directly.
    /// </summary>
    /// <param name="symbolToDelete">The symbol whose usages should be removed.</param>
    /// <param name="dependingSymbols">
    /// The set of symbol IDs that are known to contain children referencing <paramref name="symbolToDelete"/>.
    /// </param>
    /// <param name="reason">
    /// On failure, receives a description of the first encountered error; empty if the clean-up succeeded.
    /// </param>
    /// <returns><c>true</c> if all known usages were cleaned successfully; otherwise, <c>false</c>.</returns>
    private static bool CleanUsages(Symbol symbolToDelete,
                                    HashSet<Guid> dependingSymbols,
                                    out string reason)
    {
        foreach (var dependingId in dependingSymbols)
        {
            if (!SymbolRegistry.TryGetSymbol(dependingId, out var dependingSymbol))
            {
                Log.Warning($"Could not find depending symbol {dependingId} while cleaning usages of {symbolToDelete.Name}");
                continue;
            }

            // Children that use the symbol we are deleting
            var childrenUsingSymbol = dependingSymbol.Children
                                                     .Where(kvp => kvp.Value.Symbol == symbolToDelete)
                                                     .Select(kvp => kvp.Value)
                                                     .ToList();

            if (childrenUsingSymbol.Count == 0)
                continue;

            // Remove all connections to/from those children
            foreach (var child in childrenUsingSymbol)
            {
                var childId = child.Id;
                var connections = dependingSymbol.Connections.ToList();
                foreach (var c in connections)
                {
                    if (c.SourceParentOrChildId == childId ||
                        c.TargetParentOrChildId == childId)
                    {
                        dependingSymbol.RemoveConnection(c);
                    }
                }
            }

            // Remove the children themselves → this clears instances via RemoveChild()
            foreach (var child in childrenUsingSymbol)
            {
                dependingSymbol.RemoveChild(child.Id);
            }

            Log.Debug($"Disconnected and removed {childrenUsingSymbol.Count} usages of [{symbolToDelete.Name}] from [{dependingSymbol.Name}]");
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Marks all affected symbol projects (the deleted symbol's project and any projects
    /// containing depending symbols) as externally modified so that they are rebuilt and saved.
    /// </summary>
    /// <param name="dependingSymbolIds">
    /// Optional set of symbol IDs that referenced the deleted symbol and belong to additional
    /// projects that must be marked dirty, or <c>null</c> if there were no dependencies.
    /// </param>
    private static void ForceReloadSymbolLibrary(HashSet<Guid>? dependingSymbolIds)
    {
        if (_symbol == null)
            return;

        var package = _symbol.SymbolPackage;
        var affectedProjectIds = new HashSet<EditableSymbolProject>();

        // Always include the deleted symbol's own project
        var ownProject = EditableSymbolProject.AllProjects
                                              .FirstOrDefault(p => p.CsProjectFile.RootNamespace == package.RootNamespace);
        if (ownProject != null)
        {
            affectedProjectIds.Add(ownProject);
        }

        // Include projects containing depending symbols (force-delete case)
        if (dependingSymbolIds is { Count: > 0 })
        {
            foreach (var depId in dependingSymbolIds)
            {
                if (!SymbolRegistry.TryGetSymbol(depId, out var depSymbol))
                    continue;

                var depProject = EditableSymbolProject.AllProjects
                                                      .FirstOrDefault(p => p.CsProjectFile.RootNamespace == depSymbol.SymbolPackage.RootNamespace);
                if (depProject != null)
                {
                    affectedProjectIds.Add(depProject);
                }
            }
        }

        if (affectedProjectIds.Count == 0)
        {
            Log.Warning($"No EditableSymbolProject found for package {package.RootNamespace} or dependencies");
            return;
        }

        foreach (var project in affectedProjectIds)
        {
            project.MarkCodeExternallyModified();
            //Log.Info($"*** FORCED project.CodeExternallyModified = true for '{project.DisplayName}' after symbol deletion ***");
        }
    }
    #endregion
}