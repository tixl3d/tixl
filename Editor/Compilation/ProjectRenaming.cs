#nullable enable
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using T3.Core.Model;
using T3.Core.Settings;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Compilation;

/// <summary>
/// Renames a user project: the last segment of its root namespace, its .csproj, its home operator and its folder.
/// A rename is only staged while the editor runs and applied at the next start, before any project loads: the
/// running editor holds the project's assembly, file watchers and symbols under the old names, and a later save
/// would write them back.
/// </summary>
internal static class ProjectRenaming
{
    internal sealed record PendingRename(string ProjectFolder, string OldRootNamespace, string NewName);

    /// <summary>The rename staged for the project in this folder, or null. Cached, so it is cheap to call per frame.</summary>
    public static PendingRename? GetPending(string projectFolder)
    {
        foreach (var rename in Pending)
        {
            if (IsSameFolder(rename.ProjectFolder, projectFolder))
                return rename;
        }

        return null;
    }

    /// <summary>
    /// Returns why the name can't be used for a project, or null if it can. The project in
    /// <paramref name="renamedProjectFolder"/> is left out of the conflict check, so it doesn't clash with itself.
    /// </summary>
    public static string? ValidateName(string name, string? renamedProjectFolder)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";

        if (!GraphUtils.IsIdentifierValid(name))
            return "Name must be a valid C# identifier.";

        if (name.Contains('.'))
            return "Name must not contain dots.";

        // The name becomes the home symbol's class name; all-lowercase type names
        // trigger CS8981 (reserved for future language keywords) on every build.
        if (!char.IsUpper(name[0]))
            return "Name must start with a capital letter.";

        if (IsNameTaken(name, renamedProjectFolder))
            return "A project with this name already exists.";

        return null;
    }

    /// <summary>
    /// Stages renaming the project to <paramref name="newName"/> for the next start. Takes a pinned backup first,
    /// so restoring it undoes the rename. The caller saves the project before and restarts the editor after.
    /// </summary>
    public static bool TryStage(EditableSymbolProject project, string newName, out string? error)
    {
        var folder = project.Folder;
        error = ValidateName(newName, folder);
        if (error != null)
            return false;

        var parentFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        if (parentFolder != null)
        {
            var targetFolder = Path.Combine(parentFolder, newName);
            if (Directory.Exists(targetFolder) && !IsSameFolder(targetFolder, folder))
            {
                error = $"A folder named '{newName}' already exists next to the project.";
                return false;
            }
        }

        var tag = $"preRename{DateTime.Now:yyyyMMdd.HHmmss}";
        if (!Gui.AutoBackup.AutoBackup.CreatePinnedBackup(folder, tag, out _))
        {
            error = "Could not back up the project, so the rename was not started. See the log for details.";
            return false;
        }

        var pending = Pending;
        pending.RemoveAll(rename => IsSameFolder(rename.ProjectFolder, folder));
        pending.Add(new PendingRename(folder, project.CsProjectFile.RootNamespace, newName));

        try
        {
            WritePending(pending);
        }
        catch (Exception e)
        {
            error = $"Could not stage the rename: {e.Message}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the renames staged in the previous session. Call once at startup, before projects are found and loaded.
    /// A failed rename is logged and dropped, so a broken one can't repeat on every start.
    /// </summary>
    public static void ApplyPendingRenames()
    {
        var pending = Pending;
        if (pending.Count == 0)
            return;

        try
        {
            File.Delete(PendingPath);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not remove {PendingPath}, skipping project renames: {e.Message}");
            return;
        }

        foreach (var rename in pending)
        {
            try
            {
                Apply(rename);
            }
            catch (Exception e)
            {
                Log.Error($"Renaming project '{rename.OldRootNamespace}' to '{rename.NewName}' failed: {e.Message}\n"
                          + "Right-click the project and restore the 'preRename' backup to undo the partial rename.");
            }
        }

        pending.Clear();
    }

    private static void Apply(PendingRename rename)
    {
        var folder = rename.ProjectFolder;
        var oldNamespace = rename.OldRootNamespace;
        if (!Directory.Exists(folder))
        {
            Log.Warning($"Can't rename project '{oldNamespace}': its folder {folder} no longer exists.");
            return;
        }

        var csProjFiles = Directory.GetFiles(folder, "*.csproj", SearchOption.TopDirectoryOnly);
        if (csProjFiles.Length != 1)
        {
            Log.Warning($"Can't rename project '{oldNamespace}': expected one .csproj in {folder}, found {csProjFiles.Length}.");
            return;
        }

        var csProjPath = csProjFiles[0];
        var csProjText = File.ReadAllText(csProjPath);
        var oldRootNamespaceElement = $"<RootNamespace>{oldNamespace}</RootNamespace>";
        if (!csProjText.Contains(oldRootNamespaceElement, StringComparison.Ordinal))
        {
            Log.Warning($"Can't rename project '{oldNamespace}': its root namespace changed since the rename was requested.");
            return;
        }

        var lastDot = oldNamespace.LastIndexOf('.');
        var oldName = oldNamespace[(lastDot + 1)..];
        var newName = rename.NewName;
        var newNamespace = oldNamespace[..(lastDot + 1)] + newName;

        var symbolsFolder = Path.Combine(folder, FileLocations.SymbolsSubfolder);
        var rewrittenFiles = RewriteNamespace(symbolsFolder, oldNamespace, newNamespace);
        RenameHomeOperator(symbolsFolder, csProjText, oldName, newName);

        // Rename the csproj and its IDE companions (.csproj.DotSettings, .csproj.user).
        // Rewritten in place and then moved, so a case-only rename never writes the new name over the old file.
        var oldCsProjName = Path.GetFileNameWithoutExtension(csProjPath);
        File.WriteAllText(csProjPath, csProjText.Replace(oldRootNamespaceElement,
                                                         $"<RootNamespace>{newNamespace}</RootNamespace>",
                                                         StringComparison.Ordinal));
        MoveFile(csProjPath, Path.Combine(folder, newName + ".csproj"));

        // Matched by exact prefix: a "Name.csproj.*" search pattern also matches "Name.csproj" itself.
        var companionPrefix = oldCsProjName + ".csproj.";
        foreach (var companion in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(companion);
            if (!fileName.StartsWith(companionPrefix, StringComparison.Ordinal))
                continue;

            MoveFile(companion, Path.Combine(folder, newName + ".csproj." + fileName[companionPrefix.Length..]));
        }

        // The assembly is named after the root namespace; stale output under the old name must not be loaded.
        DeleteFolder(Path.Combine(folder, FileLocations.TempSubfolder, "bin"));
        DeleteFolder(Path.Combine(folder, FileLocations.TempSubfolder, "obj"));
        DeleteFolder(Path.Combine(folder, "bin"));
        DeleteFolder(Path.Combine(folder, "obj"));

        var newFolder = TryRenameFolder(folder, oldName, oldCsProjName, newName);
        if (IsSameFolder(UserSettings.Config.LastOpenProjectFolder, folder))
        {
            UserSettings.Config.LastOpenProjectFolder = newFolder;
            UserSettings.Save();
        }

        Log.Info($"Renamed project '{oldNamespace}' to '{newNamespace}' ({rewrittenFiles} files updated).");
    }

    /** Replaces the root namespace in operator sources and in type names inside symbol files. Returns the number of changed files. */
    private static int RewriteNamespace(string symbolsFolder, string oldNamespace, string newNamespace)
    {
        if (!Directory.Exists(symbolsFolder))
            return 0;

        // Whole namespace prefix only: not "Foo.User.Proj" and not "User.ProjExtras".
        var pattern = new Regex(@"(?<![\w.])" + Regex.Escape(oldNamespace) + @"(?!\w)");
        var count = 0;
        foreach (var filePath in Directory.EnumerateFiles(symbolsFolder, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(filePath);
            if (!extension.Equals(EditorSymbolPackage.SourceCodeExtension, StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(SymbolPackage.SymbolExtension, StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(EditorSymbolPackage.SymbolUiExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(filePath);
            var replaced = pattern.Replace(text, newNamespace);
            if (replaced == text)
                continue;

            File.WriteAllText(filePath, replaced);
            count++;
        }

        return count;
    }

    /// <summary>
    /// New projects name their home operator after the project. Keep that in step, but leave a home operator
    /// the user has renamed alone.
    /// </summary>
    private static void RenameHomeOperator(string symbolsFolder, string csProjText, string oldName, string newName)
    {
        if (!Directory.Exists(symbolsFolder))
            return;

        var homeGuid = Regex.Match(csProjText, @"<HomeGuid>([^<]+)</HomeGuid>").Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(homeGuid))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(symbolsFolder, oldName + EditorSymbolPackage.SourceCodeExtension, SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourcePath);
            if (!source.Contains(homeGuid, StringComparison.OrdinalIgnoreCase))
                continue;

            var directory = Path.GetDirectoryName(sourcePath)!;
            var newSourcePath = Path.Combine(directory, newName + EditorSymbolPackage.SourceCodeExtension);
            var newSymbolPath = Path.Combine(directory, newName + SymbolPackage.SymbolExtension);
            var newSymbolUiPath = Path.Combine(directory, newName + EditorSymbolPackage.SymbolUiExtension);
            if (!IsCaseOnlyChange(oldName, newName)
                && (File.Exists(newSourcePath) || File.Exists(newSymbolPath) || File.Exists(newSymbolUiPath)))
            {
                Log.Warning($"Kept the home operator name '{oldName}': an operator named '{newName}' already exists.");
                return;
            }

            File.WriteAllText(sourcePath, Regex.Replace(source, @"\b" + Regex.Escape(oldName) + @"\b", newName));
            MoveFile(sourcePath, newSourcePath);

            var symbolUiPath = Path.Combine(directory, oldName + EditorSymbolPackage.SymbolUiExtension);
            if (File.Exists(symbolUiPath))
            {
                var symbolUi = File.ReadAllText(symbolUiPath).Replace($"/*{oldName}*/", $"/*{newName}*/", StringComparison.Ordinal);
                File.WriteAllText(symbolUiPath, symbolUi);
                MoveFile(symbolUiPath, newSymbolUiPath);
            }

            var symbolPath = Path.Combine(directory, oldName + SymbolPackage.SymbolExtension);
            if (File.Exists(symbolPath))
                MoveFile(symbolPath, newSymbolPath);

            return;
        }
    }

    /// <summary>
    /// Renames the folder if it carries the project's name; a folder the user named differently keeps its name.
    /// Failing here is harmless (projects are found by their .csproj), so it only warns. Returns the final folder.
    /// </summary>
    private static string TryRenameFolder(string folder, string oldName, string oldCsProjName, string newName)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        var folderName = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed);
        if (parent == null
            || (!folderName.Equals(oldName, StringComparison.OrdinalIgnoreCase)
                && !folderName.Equals(oldCsProjName, StringComparison.OrdinalIgnoreCase)))
        {
            return folder;
        }

        var target = Path.Combine(parent, newName);
        if (folderName == newName)
            return folder;

        try
        {
            if (IsCaseOnlyChange(folderName, newName))
            {
                // Case-insensitive file systems treat a case-only rename as a move onto itself.
                var intermediate = Path.Combine(parent, newName + ".renaming");
                Directory.Move(trimmed, intermediate);
                Directory.Move(intermediate, target);
            }
            else if (Directory.Exists(target))
            {
                Log.Warning($"Kept the project folder {folder}: {target} already exists.");
                return folder;
            }
            else
            {
                Directory.Move(trimmed, target);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"The project was renamed, but its folder {folder} could not be: {e.Message}");
            return folder;
        }

        return target;
    }

    private static bool IsNameTaken(string name, string? renamedProjectFolder)
    {
        foreach (var package in SymbolPackage.AllPackages)
        {
            if (package is not EditorSymbolPackage { HasHome: true } editorPackage)
                continue;

            if (renamedProjectFolder != null && IsSameFolder(editorPackage.Folder, renamedProjectFolder))
                continue;

            if (IsLastSegment(name, editorPackage.AssemblyInformation.Name))
                return true;
        }

        foreach (var rename in Pending)
        {
            if (renamedProjectFolder != null && IsSameFolder(rename.ProjectFolder, renamedProjectFolder))
                continue;

            if (rename.NewName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsLastSegment(string name, ReadOnlySpan<char> dottedName)
    {
        var lastDot = dottedName.LastIndexOf('.');
        return dottedName[(lastDot + 1)..].Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCaseOnlyChange(string a, string b) => a != b && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameFolder(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        return IsSamePath(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b));
    }

    private static bool IsSamePath(string a, string b)
    {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveFile(string source, string target)
    {
        if (source == target)
            return;

        if (IsCaseOnlyChange(source, target))
        {
            var intermediate = target + ".renaming";
            File.Move(source, intermediate);
            File.Move(intermediate, target);
            return;
        }

        File.Move(source, target);
    }

    private static void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not delete {path}: {e.Message}");
        }
    }

    private static List<PendingRename> Pending => _pending ??= ReadPending();

    private static List<PendingRename> ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
                return JsonSerializer.Deserialize<List<PendingRename>>(File.ReadAllText(PendingPath)) ?? [];
        }
        catch (Exception e)
        {
            Log.Warning($"Could not read staged project renames from {PendingPath}: {e.Message}");
        }

        return [];
    }

    private static void WritePending(List<PendingRename> pending)
    {
        Directory.CreateDirectory(FileLocations.SettingsDirectory);
        File.WriteAllText(PendingPath, JsonSerializer.Serialize(pending, _jsonOptions));
    }

    private static string PendingPath => Path.Combine(FileLocations.SettingsDirectory, "pendingProjectRenames.json");
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private static List<PendingRename>? _pending;
}
