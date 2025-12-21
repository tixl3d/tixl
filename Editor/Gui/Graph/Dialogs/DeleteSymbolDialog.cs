#nullable enable
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
        var symbolName = symbol == null ? "undefined" : symbol.Name;

        if (dialogJustOpened)
        {
            _symbol = symbol;
        }

        LocalSymbolInfo? info = null;
        if (symbol != null && SymbolAnalysis.TryGetSymbolInfo(symbol, out var analyzedInfo))
        {
            info = new LocalSymbolInfo(analyzedInfo);
            DrawAnalysisUi(symbol, symbolName, info);
        }
        else
        {
            ImGui.Separator();
            ImGui.TextColored(UiColors.TextMuted, "Could not analyze dependencies for this symbol.");
            _allowDeletion = false;
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace(4);

        // Single delete button handles both normal and force delete
        ImGui.BeginDisabled(!_allowDeletion);
        string buttonLabel = "Delete";
        if (info != null && info.DependingSymbols.Any())
        {
            buttonLabel = "Force delete##1";
        }
        
        if (ImGui.Button(buttonLabel) && _allowDeletion && symbol != null)
        {
            bool success;
            string reason;

            if (info != null && info.DependingSymbols.Any())
            {
                success = DeleteSymbol(symbol, info.DependingSymbols, out reason);
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
        
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            Close();
        }

        EndDialogContent();
        EndDialog();
    }

    // Draws the context once SymbolAnalysis.TryGetSymbolInfo has succeeded
    private static void DrawAnalysisUi(Symbol symbol, string symbolName, LocalSymbolInfo info)
    {
        var restriction      = GetRestriction(symbol, info);
        var isProtected      = restriction is not null;
        var isNamespaceMain  = IsNamespaceMainSymbol(symbol);

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
            ImGui.TextWrapped(
                $"Symbol [{symbolName}] acts as the main symbol for namespace [{symbol.Namespace}]. \n" +
                "Removing it directly can leave the project in a broken state. \n" +
                "Use the namespace delete workflow (todo) instead of deleting this symbol.");
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
                ImGui.TextWrapped("Clicking Delete will force-delete this symbol and automatically disconnect/clean all usages. " +
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
                ImGui.TextWrapped($"Symbol [{symbolName}] is not used by other symbols. \nThis is safe to delete.");
                ImGui.PopStyleColor();
            }
        }
    }

    private static string? GetRestriction(Symbol symbol, LocalSymbolInfo info)
    {
        if (info.IsLibOperator)
            return "part of the Main Library";
        if (info.IsTypeOperator)
            return "part of the Types Library";
        if (info.IsExampleOperator)
            return "part of the Examples Library";
        if (info.IsT3Operator)
            return "part of the T3 Library";
        if (info.IsSkillOperator)
            return "part of the Skills Library";

        if (IsNamespaceMainSymbol(symbol))
            return "the main symbol of this namespace and attached to the project";
        if (symbol.SymbolPackage.IsReadOnly)
            return "Read Only";

        return null;
    }

    private static bool IsNamespaceMainSymbol(Symbol symbol)
    {
        var ns = symbol.Namespace ?? string.Empty;
        if (string.IsNullOrEmpty(ns))
            return false;

        var lastDotIndex = ns.LastIndexOf('.');
        var lastSegment  = lastDotIndex >= 0 ? ns[(lastDotIndex + 1)..] : ns;
        return string.Equals(lastSegment, symbol.Name, StringComparison.Ordinal);
    }

    private sealed class LocalSymbolInfo
    {
        public HashSet<Guid> DependingSymbols { get; }
        public int UsageCount { get; }
        public bool IsLibOperator { get; }
        public bool IsTypeOperator { get; }
        public bool IsExampleOperator { get; }
        public bool IsT3Operator { get; }
        public bool IsSkillOperator { get; }

        public LocalSymbolInfo(SymbolAnalysis.SymbolInformation source)
        {
            DependingSymbols   = source.DependingSymbols;
            UsageCount         = source.UsageCount;
            IsLibOperator      = source.IsLibOperator;
            IsTypeOperator     = source.IsTypeOperator;
            IsExampleOperator  = source.IsExampleOperator;
            IsT3Operator       = source.IsT3Operator;
            IsSkillOperator    = source.IsSkillOperator;
        }
    }

    private static void ListSymbolNames(IEnumerable<Guid> symbolIds)
    {
        var allSymbolUis = EditorSymbolPackage.AllSymbolUis;
        var idSet = symbolIds.ToHashSet();
        var matches = allSymbolUis
                      .Where(s => idSet.Contains(s.Symbol.Id))
                      .OrderBy(s => s.Symbol.Namespace)
                      .ThenBy(s => s.Symbol.Name)
                      .ToList();

        if (!matches.Any())
            return;

        const float itemHeight = 20.0f;
        const int   maxVisibleItems = 6;
        var scrollHeight = itemHeight * maxVisibleItems;

        if (ImGui.BeginChild("SymbolList",
                new System.Numerics.Vector2(0, scrollHeight),
                true))
        {
            var lastGroupName = string.Empty;
            foreach (var symbolUi in matches)
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
                    var nsLabelBgColor = UiColors.BackgroundFull;
                    nsLabelBgColor.A = 0.3f;
                    drawList.AddRectFilled(rectMin, rectMax, nsLabelBgColor, 0.0f);

                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextUnformatted(projectName);
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                }

                var hasIssues = symbolUi.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                                || symbolUi.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix);
                var color = hasIssues ? UiColors.StatusAttention : UiColors.Text;

                ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted(" " + symbolUi.Symbol.Name);
                ImGui.PopFont();
                ImGui.PopStyleColor();
            }
        }

        ImGui.EndChild();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deletion helpers
    // ─────────────────────────────────────────────────────────────────────────
    
    // Unified delete: optional dependency clean-up, then delete+reload
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

    // Physical file deletion and bin/obj cleanup
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

            // File name is just symbol.Name.* (no namespace parts in file name)
            var basePath = Path.Combine(fullSymbolFolder, symbol.Name);

            var csPath   = basePath + ".cs";
            var t3Path   = basePath + ".t3";
            var t3UiPath = basePath + ".t3ui";

            if (!File.Exists(csPath))
            {
                reason = $"Could not locate the source file for symbol [{symbol.Name}]\n" +
                         $"RootNamespace: [{rootNs}]\n" +
                         $"Namespace: [{symbolNs}]\n" +
                         $"Expected at: {csPath}";
                return false;
            }

            var projectRoot = Path.GetDirectoryName(symbol.SymbolPackage.Folder);
            if (string.IsNullOrEmpty(projectRoot))
            {
                reason = $"Could not determine the directory for symbol [{symbol.Name}]";
                return false;
            }

            if (File.Exists(csPath))
            {
                File.Delete(csPath);
                Log.Debug($"Deleted: '{csPath}'");
            }

            if (File.Exists(t3Path))
            {
                File.Delete(t3Path);
                Log.Debug($"Deleted: '{t3Path}'");
            }

            if (File.Exists(t3UiPath))
            {
                File.Delete(t3UiPath);
                Log.Debug($"Deleted: '{t3UiPath}'");
            }

            var binDir = Path.Combine(projectRoot, "bin");
            var objDir = Path.Combine(projectRoot, "obj");

            if (Directory.Exists(binDir))
            {
                Directory.Delete(binDir, recursive: true);
                Log.Debug($"Deleted: '{binDir}'");
            }

            if (Directory.Exists(objDir))
            {
                Directory.Delete(objDir, recursive: true);
                Log.Debug($"Deleted: '{objDir}'");
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

    /// <summary>
    /// Disconnects all connections involving children that reference the symbol being deleted.
    /// Does not call internal runtime methods or mutate Children directly.
    /// </summary>
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
    /// Marks all affected projects (the deleted symbol's project and any projects
    /// that contain depending symbols) as externally modified and triggers save/recompile.
    /// </summary>
    private static void ForceReloadSymbolLibrary(HashSet<Guid>? dependingSymbols)
    {
        if (_symbol == null)
            return;

        var package = _symbol.SymbolPackage;
        var affectedProjects = new HashSet<EditableSymbolProject>();

        // Always include the deleted symbol's own project
        var ownProject = EditableSymbolProject.AllProjects
                                              .FirstOrDefault(p => p.CsProjectFile.RootNamespace == package.RootNamespace);
        if (ownProject != null)
        {
            affectedProjects.Add(ownProject);
        }

        // Include projects containing depending symbols (force-delete case)
        if (dependingSymbols is { Count: > 0 })
        {
            foreach (var depId in dependingSymbols)
            {
                if (!SymbolRegistry.TryGetSymbol(depId, out var depSymbol))
                    continue;

                var depProject = EditableSymbolProject.AllProjects
                                                      .FirstOrDefault(p => p.CsProjectFile.RootNamespace == depSymbol.SymbolPackage.RootNamespace);
                if (depProject != null)
                {
                    affectedProjects.Add(depProject);
                }
            }
        }

        if (affectedProjects.Count == 0)
        {
            Log.Warning($"No EditableSymbolProject found for package {package.RootNamespace} or dependencies");
            return;
        }

        foreach (var project in affectedProjects)
        {
            project.MarkCodeExternallyModified();
            //Log.Info($"*** FORCED project.CodeExternallyModified = true for '{project.DisplayName}' after symbol deletion ***");
        }
    }

    private static void Close()
    {
        ImGui.CloseCurrentPopup();
        _symbol = null;
        _allowDeletion = false;
    }

    private static Symbol? _symbol;
    private static bool _allowDeletion;
}
