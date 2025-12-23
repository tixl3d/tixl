#nullable enable
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Dialogs;

internal sealed class DeleteSymbolDialog : ModalDialog
{
    internal void Draw(Symbol symbol)
    {
        if (!BeginDialog("Delete Symbol"))
        {
            EndDialog();
            return;
        }

        var dialogJustOpened = _symbol == null;

        if (dialogJustOpened)
        {
            _symbol = symbol;
        }

        LocalSymbolInfo? info = null;
        if (symbol != null && SymbolAnalysis.TryGetSymbolInfo(symbol, out var analyzedInfo, true))
        {
            info = new LocalSymbolInfo(analyzedInfo);
            DrawAnalysisUi(symbol, info);
        }
        else
        {
            ImGui.Separator();
            ImGui.TextColored(UiColors.TextMuted, "Could not analyze dependencies for this symbol.");
            _allowDeletion = false;
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace(4);

        // Only draw buttons if deletion is allowed
        if (_allowDeletion)
        {
            string buttonLabel = "Delete";
            if (info != null && info.DependingSymbols.Any())
            {
                buttonLabel = "Force delete";
            }
            
            if (ImGui.Button(buttonLabel) && symbol != null)
            {
                bool success;
                string reason;

                if (info?.DependingSymbols.Any() == true)
                {
                    success = DeleteSymbol(symbol, info.DependingSymbols.ToHashSet(), out reason);
                }
                else
                {
                    success = DeleteSymbol(symbol, null, out reason);
                }
    
                if (!success)
                {
                    BlockingWindow.Instance.ShowMessageBox(reason, "Could not delete symbol");
                }
                else
                {
                    Close();
                }
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Cancel"))
        {
            Close();
        }

        EndDialogContent();
        EndDialog();
    }
    
    /// <summary>
    /// Renders the main body of the delete confirmation dialog after symbol dependency analysis
    /// has completed successfully.
    /// </summary>
    /// <param name="symbol">The symbol currently selected for deletion.</param>
    /// <param name="info">Detailed dependency and classification information about the symbol
    /// obtained from <see cref="SymbolAnalysis.TryGetSymbolInfo"/>.</param>
    private static void DrawAnalysisUi(Symbol symbol, LocalSymbolInfo info)
    {
        var isProtected = TryGetRestriction(symbol, info, out var restriction);
        var isNamespaceMain = IsNamespaceMainSymbol(symbol);
        var symbolName = symbol == null ? "undefined" : symbol.Name;

        _allowDeletion = !isProtected && !symbol.SymbolPackage.IsReadOnly;

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        if (isProtected)
        {
            ImGui.TextWrapped($"Can not delete [{symbolName}]");
        }
        else
        {
            ImGui.TextWrapped($"Are you sure you want to delete [{symbolName}]?");
        }
        ImGui.PopStyleColor();

        if (isProtected)
        {
            ImGui.PushFont(Fonts.FontBold);
            if (isNamespaceMain)
            {
                ImGui.TextColored(
                                  UiColors.StatusAttention,
                                  "This symbol is attached to the project namespace.");
            }
            else
            {
                ImGui.TextColored(
                                  UiColors.StatusAttention,
                                  $"You can not delete symbols that are {restriction}");
            }
            ImGui.PopFont();
        }

        if (isNamespaceMain)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            ImGui.TextWrapped($"""
                              Symbol [{symbolName}] acts as the main symbol for namespace [{symbol.Namespace}]. 
                              Removing it directly can leave the project in a broken state. 
                              Use the namespace delete workflow (todo) instead of deleting this symbol.
                              """);

            ImGui.PopStyleColor();

            return;
        }

        if (info.DependingSymbols.Any())
        {
            if (!_allowDeletion)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped(
                    $"Symbol [{symbolName}] is used by [{info.DependingSymbols.Count}] other projects/symbols, " +
                    "but deletion is blocked because it belongs to a protected library or read-only package.");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped($"[{symbolName}] is used in [{info.DependingSymbols.Count}] projects/symbols:");
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ListSymbolNames(info.DependingSymbols);
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped("Clicking Force delete will automatically disconnect/clean all usages. " +
                                  "This may completely break these projects/symbols, and can *NOT* be undone.");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            if (!_allowDeletion)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped($"Symbol [{symbolName}] is not used by other symbols, " +
                                  $"but deletion is blocked because it belongs to a protected library or read-only package.");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                ImGui.TextWrapped($"Symbol [{symbolName}] is not used by other symbols and can be safely deleted.");
                ImGui.PopStyleColor();
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve a human-readable restriction reason for the given symbol, such as library membership or read-only status.
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
        restriction = info.OperatorFlags switch
                          {
                              OperatorType.Lib                       => "part of the Main Library",
                              OperatorType.Type                      => "part of the Types Library",
                              OperatorType.Example                   => "part of the Examples Library",
                              OperatorType.T3                        => "part of the T3 Library",
                              OperatorType.Skill                     => "part of the Skills Library",
                              _ when IsNamespaceMainSymbol(symbol)   => "the main symbol of this namespace and attached to the project",
                              _ when symbol.SymbolPackage.IsReadOnly => "Read Only",
                              _                                      => null
                          };

        return restriction != null;
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

    /// <summary>
    /// Represents operator categories for symbols in the analysis system.
    /// Supports multiple simultaneous classifications via bit flags (Lib, Type, Example, T3, Skill).
    /// Use HasFlag() to test individual categories or bitwise OR to combine during construction.
    /// </summary>
    [Flags]
    private enum OperatorType
    {
        None = 0,
        Lib = 1 << 0,
        Type = 1 << 1,
        Example = 1 << 2,
        T3 = 1 << 3,
        Skill = 1 << 4
    }
    
    /// <summary>
    /// Lightweight container for symbol dependency and classification data used by the dialog,
    /// derived from <see cref="SymbolAnalysis.SymbolInformation"/>.
    /// </summary>
    private sealed class LocalSymbolInfo(SymbolAnalysis.SymbolInformation source)
    {
        public ImmutableHashSet<Guid> DependingSymbols { get; } = ImmutableHashSet.CreateRange(source.DependingSymbols);
        public int UsageCount { get; } = source.UsageCount;
        public OperatorType OperatorFlags { get; } = (source.IsLibOperator ? OperatorType.Lib : 0) |
                                                     (source.IsTypeOperator ? OperatorType.Type : 0) |
                                                     (source.IsExampleOperator ? OperatorType.Example : 0) |
                                                     (source.IsT3Operator ? OperatorType.T3 : 0) |
                                                     (source.IsSkillOperator ? OperatorType.Skill : 0);
    }

    /// <summary>
    /// Renders a scrollable list of symbol names and their project namespaces for all
    /// symbols that reference the symbol being deleted.
    /// </summary>
    /// <param name="symbolIds">
    /// The set of symbol IDs that depend on the symbol currently considered for deletion.
    /// </param>
    private static void ListSymbolNames(IEnumerable<Guid> symbolIds)
    {

        if (_cachedMatches == null)
        {
            var allSymbolUis = EditorSymbolPackage.AllSymbolUis;
            var idSet = symbolIds.ToHashSet();
            _cachedMatches = allSymbolUis
                            .Where(s => idSet.Contains(s.Symbol.Id))
                            .OrderBy(s => s.Symbol.Namespace)
                            .ThenBy(s => s.Symbol.Name)
                            .ToList();
        }

        if (!_cachedMatches.Any())
            return;

        const float itemHeight = 20.0f;
        const int   maxVisibleItems = 6;
        var scrollHeight = itemHeight * maxVisibleItems;

        if (ImGui.BeginChild("SymbolList",
                new System.Numerics.Vector2(0, scrollHeight),
                true))
        {
            var lastGroupName = string.Empty;
            foreach (var symbolUi in _cachedMatches)
            {
                var projectName = symbolUi.Symbol.SymbolPackage.RootNamespace;
                if (projectName != lastGroupName)
                {
                    lastGroupName = projectName;
                    var avail    = ImGui.GetContentRegionAvail();
                    var cursorPos = ImGui.GetCursorScreenPos();
                    var drawList  = ImGui.GetWindowDrawList();
                    var rectMin   = cursorPos;
                    var rectMax   = new System.Numerics.Vector2(
                        cursorPos.X + avail.X,
                        cursorPos.Y + Fonts.FontSmall.FontSize + 4);
                    var nsLabelBgColor = UiColors.BackgroundFull.Fade(0.3f);
                    drawList.AddRectFilled(rectMin, rectMax, nsLabelBgColor, 0.0f);

                    CustomComponents.StylizedText(projectName, Fonts.FontSmall, UiColors.Text);
                    
                }

                var hasIssues = symbolUi.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                                || symbolUi.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix);
                var color = hasIssues ? UiColors.StatusAttention : UiColors.Text;

                var symbolLabel = "  " + symbolUi.Symbol.Name;
                CustomComponents.StylizedText(symbolLabel, Fonts.FontSmall, color);
            }
        }

        ImGui.EndChild();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deletion helpers
    // ─────────────────────────────────────────────────────────────────────────
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
            var packageFolder = symbol.SymbolPackage.Folder;
            var rootNs = symbol.SymbolPackage.RootNamespace ?? string.Empty;
            var symbolNs = symbol.Namespace ?? string.Empty;

            var rootParts = string.IsNullOrEmpty(rootNs)
                                ? Array.Empty<string>()
                                : rootNs.Split('.');
            var symbolParts = string.IsNullOrEmpty(symbolNs)
                                ? Array.Empty<string>()
                                : symbolNs.Split('.');

            // Strip root namespace prefix from symbol namespace
            int i = 0;
            while (i < rootParts.Length && i < symbolParts.Length &&
                   string.Equals(rootParts[i], symbolParts[i], StringComparison.Ordinal))
            {
                i++;
            }

            var relativeParts = symbolParts.Skip(i).ToArray();
            string relativePath = relativeParts.Length > 0
                                      ? Path.Combine(relativeParts)
                                      : string.Empty;

            var fullSymbolFolder = string.IsNullOrEmpty(relativePath)
                                       ? packageFolder
                                       : Path.Combine(packageFolder, relativePath);
            
            var basePath = Path.Combine(fullSymbolFolder, symbol.Name);

            var csPath   = basePath + ".cs";
            var t3Path   = basePath + ".t3";
            var t3UiPath = basePath + ".t3ui";
            
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
    /// <param name="dependingSymbols">
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
    
    /// <summary>
    /// Closes the delete-symbol dialog and resets its internal state so a new delete session
    /// starts with a clean symbol reference and deletion flag.
    /// </summary>
    private static void Close()
    {
        ImGui.CloseCurrentPopup();
        _symbol = null;
        _allowDeletion = false;
        _cachedMatches = null;
    }

    private static Symbol? _symbol;
    private static bool _allowDeletion;
    private static List<SymbolUi>? _cachedMatches;
}
