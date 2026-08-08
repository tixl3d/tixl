#nullable enable

using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Compilation;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Hub;

[HelpUiID("ProjectPanel")]
internal static class ProjectsPanel
{
    public static void Draw(GraphWindow window)
    {
        var heightForSkillQuest = UserSettings.Config.ShowSkillQuestInHub ? SkillQuestPanel.Height + 10 : 0;

        ContentPanel.Begin("Projects", null, DrawProjectTools, -heightForSkillQuest);

        FormInputs.AddVerticalSpace(20);

        ImGui.BeginChild("content", new Vector2(0, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoBackground);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 5));
            
            foreach (var package in EditableSymbolProject.AllProjects)
            {
                DrawProjectItem(window, package);
            }
            
            // 2. Draw Archived Projects (Lightweight)
            if (ProjectSetup.ArchivedProjects.Count > 0)
            {
                ImGui.Separator();
                FormInputs.AddSectionHeader("Archived");
                ImGui.TextDisabled("Note: Loading reactivated projects might require a restart.");
                for (var index = 0; index < ProjectSetup.ArchivedProjects.Count; index++)
                {
                    var archived = ProjectSetup.ArchivedProjects[index];
                    DrawArchivedItem(archived);
                }
            }

            // 3. Draw Broken Projects (failed to load — offer recovery)
            if (ProjectSetup.BrokenProjects.Count > 0)
            {
                ImGui.Separator();
                FormInputs.AddSectionHeader("Broken");
                ImGui.TextDisabled("These projects could not be loaded. Right-click to restore an earlier backup.");
                foreach (var broken in ProjectSetup.BrokenProjects)
                {
                    DrawBrokenItem(broken);
                }
            }

            ImGui.PopStyleVar();
        }
        ImGui.EndChild();
        ContentPanel.End();
    }

    private static void DrawProjectTools()
    {
        var addProject = "Add Project...";
        var size = CustomComponents.GetCtaButtonSize(addProject);
        var state = EditableSymbolProject.AllProjects.Any()
                        ? CustomComponents.ButtonStates.Default
                        : CustomComponents.ButtonStates.Activated;
        CustomComponents.RightAlign(size.X + 10 * T3Ui.UiScaleFactor);
        if (CustomComponents.DrawCtaButton(addProject, Icon.None, state))
        {
            T3Ui.NewProjectDialog.ShowNextFrame();
        }
    }

    /// <summary>Draws one project row. Returns true when a click loaded the project into the given window.</summary>
    internal static bool DrawProjectItem(GraphWindow window, EditorSymbolPackage package)
    {
        if (!package.HasHome)
            return false;

        var dl = ImGui.GetWindowDrawList();

        ImGui.PushID(package.DisplayName);
        var isOpened = OpenedProject.OpenedProjects.TryGetValue(package, out var openedProject);
        var name = package.DisplayName;
        var clicked = ImGui.InvisibleButton(name, ProjectItemSize);
        var isHovered = ImGui.IsItemHovered();
        var backgroundColor = isHovered
                                  ? UiColors.ForegroundFull.Fade(0.1f)
                                  : UiColors.ForegroundFull.Fade(0.05f);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, backgroundColor, 6);

        var padding = 3f * T3Ui.UiScaleFactor;
        var hasCorruptFiles = package.HasCorruptedSymbolFiles;
        if (hasCorruptFiles || isOpened)
        {
            // Magenta "needs attention" bar for a project with corrupt files; the normal active bar otherwise.
            dl.AddRectFilled(min + Vector2.One * padding,
                             new Vector2(min.X + padding + 4, max.Y - padding),
                             hasCorruptFiles ? UiColors.StatusAttention : UiColors.BackgroundActive, 2);
        }

        var rootName = package.RootNamespace.Split(".")[^1];
        if (isOpened)
            rootName += " (loaded)";

        var y = padding;
        var x = 20f;
        dl.AddText(Fonts.FontBold,
                   Fonts.FontBold.FontSize,
                   min + new Vector2(x, y),
                   UiColors.Text, rootName);

        y += Fonts.FontNormal.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, package.RootNamespace);

        y += Fonts.FontSmall.FontSize + 5;

        if (hasCorruptFiles)
        {
            var count = package.CorruptedSymbolFilePaths.Count;
            var corruptLabel = count == 1
                                   ? "1 corrupt file - restore from backup"
                                   : $"{count} corrupt files - restore from backup";
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, min + new Vector2(x, y),
                       UiColors.StatusAttention, corruptLabel);
        }
        else
        {
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, min + new Vector2(x, y),
                       UiColors.TextMuted, GetSavedInfoLabel(package));
        }

        if (hasCorruptFiles && isHovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextColored(UiColors.StatusAttention, "Corrupt operator file(s) - restore an earlier backup to recover:");
            foreach (var path in package.CorruptedSymbolFilePaths)
                ImGui.TextUnformatted(path);
            ImGui.EndTooltip();
        }

        var thumbnail = ThumbnailManager.GetThumbnail(package.Id, package, ThumbnailManager.Categories.PackageMeta);
        if (thumbnail.IsReady && ThumbnailManager.AtlasSrv != null)
        {
            var height = ProjectItemSize.Y - padding * 2;
            var size = new Vector2(height * 4 / 3f, height);
            var pos = new Vector2(max.X - size.X - padding, min.Y + padding);
            dl.AddImage(ThumbnailManager.AtlasSrv.NativePointer,
                        pos,
                        pos + size,
                        thumbnail.UvMin, thumbnail.UvMax);
        }

        var wasOpened = false;
        if (clicked && hasCorruptFiles)
        {
            // Don't open a project with corrupt files — it would only show the empty shell that loaded
            // in their place. Recovery is restore-from-backup (right-click), then restart.
            Log.Warning($"'{package.DisplayName}' has corrupt operator file(s) and can't be opened. "
                        + "Right-click it and restore an earlier backup first.");
        }
        else if (clicked)
        {
            // No early return on failure — the PushID/PopID pair below must stay balanced.
            if (!isOpened && package is EditorSymbolPackage editorPackage2)
            {
                if (!OpenedProject.TryCreate(editorPackage2, out openedProject, out var error))
                {
                    Log.Warning($"Failed to load project: {error}");
                }
            }

            if (openedProject != null)
            {
                wasOpened = window.TrySetToProject(openedProject);
            }
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        if (ImGui.BeginPopupContextItem("windows_context_menu"))
        {
            if (CustomComponents.DrawMenuItem(_revealInExplorerId, "Reveal in Explorer", reserveIconColumn: false))
            {
                CoreUi.Instance.OpenWithDefaultApplication(package.Folder);
            }

            if (CustomComponents.DrawSubMenu(_restoreFromBackupId, "Restore from Backup"))
            {
                DrawRestoreBackupItems(package.DisplayName, package.Folder);
                ImGui.EndMenu();
            }

            if (CustomComponents.DrawMenuItem(_unloadProjectId, "Unload Project", isEnabled: isOpened, reserveIconColumn: false))
            {
                if (OpenedProject.TryUnload(package))
                {
                    Log.Debug($"Project '{package.DisplayName}' unloaded successfully");
                }
            }

            if(CustomComponents.DrawMenuItem(_archiveProjectId, "Archive Project", reserveIconColumn: false))
            {
                if (package is EditableSymbolProject editableProject)
                {
                    if (isOpened)
                        OpenedProject.TryUnload(package);
                    
                    ProjectSetup.SetProjectArchived(editableProject.CsProjectFile, true);
                }
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();

        ImGui.PopID();
        return wasOpened;
    }

    private static void DrawArchivedItem(ProjectSetup.ArchivedProjectInfo archivedProject)
    {
        var dl = ImGui.GetWindowDrawList();

        ImGui.PushID(archivedProject.Name);
        var name = archivedProject.Name;
        var clicked = ImGui.InvisibleButton(name, ProjectItemSize);
        var isHovered = ImGui.IsItemHovered();
        var backgroundColor = isHovered
                                  ? UiColors.ForegroundFull.Fade(0.1f)
                                  : UiColors.ForegroundFull.Fade(0.05f);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, backgroundColor, 6);

        var padding = 3f * T3Ui.UiScaleFactor;
        
        var rootName = archivedProject.RootNamespace?.Split(".")[^1];

        var y = padding;
        var x = 20f;
        dl.AddText(Fonts.FontBold,
                   Fonts.FontBold.FontSize,
                   min + new Vector2(x, y),
                   UiColors.Text, rootName);

        y += Fonts.FontNormal.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, archivedProject.Folder);

        y += Fonts.FontSmall.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, archivedProject.Folder);

        var thumbnail = ThumbnailManager.GetThumbnail(archivedProject.Id, archivedProject, ThumbnailManager.Categories.PackageMeta);
        if (thumbnail.IsReady && ThumbnailManager.AtlasSrv != null)
        {
            var height = ProjectItemSize.Y - padding * 2;
            var size = new Vector2(height * 4 / 3f, height);
            var pos = new Vector2(max.X - size.X - padding, min.Y + padding);
            dl.AddImage(ThumbnailManager.AtlasSrv.NativePointer,
                        pos,
                        pos + size,
                        thumbnail.UvMin, thumbnail.UvMax);
        }
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        if (ImGui.BeginPopupContextItem("windows_context_menu"))
        {
            if(CustomComponents.DrawMenuItem(_reactivateProjectId, "Reactivate Project", reserveIconColumn: false))
            {
                ProjectSetup.SetProjectArchived(archivedProject.ProjectFile, false);
            }
            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();

        ImGui.PopID();
    }

    private static void DrawBrokenItem(ProjectSetup.BrokenProjectInfo broken)
    {
        var dl = ImGui.GetWindowDrawList();

        ImGui.PushID(broken.Folder);
        ImGui.InvisibleButton(broken.Name, ProjectItemSize);
        var isHovered = ImGui.IsItemHovered();
        var backgroundColor = isHovered
                                  ? UiColors.ForegroundFull.Fade(0.1f)
                                  : UiColors.ForegroundFull.Fade(0.05f);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, backgroundColor, 6);

        // A magenta bar on the left flags "needs attention", matching the status-color convention.
        var padding = 3f * T3Ui.UiScaleFactor;
        dl.AddRectFilled(min + Vector2.One * padding,
                         new Vector2(min.X + padding + 4, max.Y - padding),
                         UiColors.StatusAttention, 2);

        var y = padding;
        var x = 20f;
        dl.AddText(Fonts.FontBold, Fonts.FontBold.FontSize, min + new Vector2(x, y), UiColors.Text, broken.Name);

        y += Fonts.FontNormal.FontSize + 5;
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, min + new Vector2(x, y),
                   UiColors.TextMuted, broken.RootNamespace ?? broken.Folder);

        y += Fonts.FontSmall.FontSize + 5;
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, min + new Vector2(x, y),
                   UiColors.StatusAttention, broken.Reason);

        if (isHovered && !string.IsNullOrEmpty(broken.Hint))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(broken.Hint);
            ImGui.EndTooltip();
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        if (ImGui.BeginPopupContextItem("broken_context_menu"))
        {
            if (CustomComponents.DrawSubMenu(_restoreBrokenFromBackupId, "Restore from Backup"))
            {
                DrawRestoreBackupItems(broken.Name, broken.Folder);
                ImGui.EndMenu();
            }

            if (CustomComponents.DrawMenuItem(_revealBrokenInExplorerId, "Reveal in Explorer", reserveIconColumn: false))
            {
                CoreUi.Instance.OpenWithDefaultApplication(broken.Folder);
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();

        ImGui.PopID();
    }

    public static Vector2 ProjectItemSize => new Vector2(400, 65) * T3Ui.UiScaleFactor;

    /// <summary>
    /// "Saved 23 days ago with v4.2" for a project row. Composing it needs file mtime IO, so the
    /// label is cached per project and refreshed only every few seconds.
    /// </summary>
    private static string GetSavedInfoLabel(EditorSymbolPackage package)
    {
        var now = ImGui.GetTime();
        if (_savedInfoLabels.TryGetValue(package.Folder, out var cached) && now - cached.LastUpdate < 10)
            return cached.Label;

        var label = BuildSavedInfoLabel(package);
        _savedInfoLabels[package.Folder] = (now, label);
        return label;
    }

    private static string BuildSavedInfoLabel(EditorSymbolPackage package)
    {
        DateTime? savedAt = null;
        try
        {
            // The editor rewrites the csproj on every save, so its mtime tracks the last save.
            foreach (var csproj in Directory.EnumerateFiles(package.Folder, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                savedAt = File.GetLastWriteTime(csproj);
                break;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read save time for {package.DisplayName}: {e.Message}");
        }

        var savedPart = savedAt == null
                            ? "Save time unknown"
                            : $"Saved {savedAt.GetReadableRelativeTime()}";

        if (package.AssemblyInformation.TryGetReleaseInfo(out var releaseInfo))
        {
            return $"{savedPart} with v{releaseInfo.EditorVersion.Major}.{releaseInfo.EditorVersion.Minor}";
        }

        return savedPart;
    }

    /// <summary>
    /// Items of the "Restore from backup" submenu, newest first. Labels and the underlying
    /// directory scan are cached and refreshed only every couple of seconds — the submenu body
    /// runs every frame while open and must not do per-frame IO or string building.
    /// </summary>
    private static void DrawRestoreBackupItems(string displayName, string folder)
    {
        var now = ImGui.GetTime();
        if (_backupMenuFolder != folder || now - _backupMenuUpdateTime > 2)
        {
            RefreshBackupMenuItems(folder);
            _backupMenuFolder = folder;
            _backupMenuUpdateTime = now;
        }

        if (_backupMenuItems.Count == 0)
        {
            CustomComponents.DrawMenuItem(_noBackupsFoundId, "No backups found", isEnabled: false, reserveIconColumn: false);
            return;
        }

        for (var index = 0; index < _backupMenuItems.Count; index++)
        {
            var (label, backup) = _backupMenuItems[index];

            // Pre-restore snapshots are the "before I restored" safety copies — often of the very state
            // being recovered from — so mute them; they're rarely the version to restore *to*.
            var isPreRestore = backup.KeepTag != null && backup.KeepTag.StartsWith("preRestore", StringComparison.Ordinal);

            if (CustomComponents.DrawMenuItem(_backupItemBaseId + index,
                                              Icon.None,
                                              label,
                                              reserveIconColumn: false,
                                              state: isPreRestore
                                                         ? CustomComponents.ButtonStates.Default
                                                         : CustomComponents.ButtonStates.Emphasized))
            {
                T3Ui.RestoreBackupDialog.ShowNextFrame(displayName, folder, backup);
            }

            if (ImGui.IsItemHovered())
                DrawBackupTooltip(backup);
        }
    }

    /// <summary>
    /// Hover detail for a backup: absolute timestamp plus operator/connection counts. The counts need
    /// decompressing the .t3 files, so they're computed lazily (only for hovered backups) on a
    /// background thread and shown as "counting..." until ready.
    /// </summary>
    private static void DrawBackupTooltip(AutoBackup.AutoBackup.BackupEntry backup)
    {
        QueueBackupContentStats(backup.FilePath);

        ImGui.BeginTooltip();

        var absolute = backup.Timestamp == DateTime.MinValue
                           ? "Unknown time"
                           : backup.Timestamp.ToString("yyyy-MM-dd  HH:mm:ss");
        ImGui.TextUnformatted(absolute);

        var detail = _backupContentStats.TryGetValue(backup.FilePath, out var stats)
                         ? $"{stats.Operators} operators, {stats.Connections} connections"
                         : "counting...";
        ImGui.TextColored(UiColors.TextMuted, detail);

        ImGui.EndTooltip();
    }

    private static void RefreshBackupMenuItems(string projectFolder)
    {
        _backupMenuItems.Clear();
        foreach (var backup in AutoBackup.AutoBackup.EnumerateBackupsFor(projectFolder))
        {
            _backupMenuItems.Add((BuildBackupMenuLabel(backup), backup));
        }
    }

    private static string BuildBackupMenuLabel(AutoBackup.AutoBackup.BackupEntry backup)
    {
        var when = backup.Timestamp == DateTime.MinValue
                       ? "unknown time"
                       : ((DateTime?)backup.Timestamp).GetReadableRelativeTime();

        var symbolCount = GetBackupSymbolCount(backup.FilePath);
        var symbolsPart = symbolCount < 0 ? "" : $" · {symbolCount} symbols";

        var markers = "";
        if (backup.IsMinimal)
            markers += " · minimal";
        var keepLabel = FriendlyKeepTag(backup.KeepTag);
        if (keepLabel != null)
            markers += $" · {keepLabel}";

        // Operator/connection counts need decompression and are shown in the hover tooltip instead.
        return $"v{backup.Index}   {when}{symbolsPart} · {FormatSize(backup.SizeBytes)}{markers}";
    }

    /// <summary>Human-readable label for a pinned backup's keep-tag ("preRestore…" / "preFormat…").</summary>
    private static string? FriendlyKeepTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        if (tag.StartsWith("preRestore", StringComparison.Ordinal))
            return "pre-restore";
        if (tag.StartsWith("preFormat", StringComparison.Ordinal))
            return "pre-format-upgrade";
        return "kept";
    }

    /// <summary>Number of symbol (.t3) files in a backup zip, from the zip index only. Cached by path (zips are immutable).</summary>
    private static int GetBackupSymbolCount(string zipPath)
    {
        if (_backupSymbolCountsByPath.TryGetValue(zipPath, out var cached))
            return cached;

        var count = 0;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.EndsWith(".t3", StringComparison.OrdinalIgnoreCase))
                    count++;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read backup archive {zipPath}: {e.Message}");
            count = -1;
        }

        _backupSymbolCountsByPath[zipPath] = count;
        return count;
    }

    /// <summary>
    /// Counts operators (symbol-children) and connections inside a backup's .t3 files. This needs
    /// decompression, so it runs once on a background thread and caches by path (zips are immutable);
    /// the menu label picks the result up on its next refresh.
    /// </summary>
    private static void QueueBackupContentStats(string zipPath)
    {
        if (_backupContentStats.ContainsKey(zipPath) || !_contentStatsQueued.Add(zipPath))
            return;

        Task.Run(() =>
                 {
                     try
                     {
                         _backupContentStats[zipPath] = ComputeBackupContentStats(zipPath);
                     }
                     catch (Exception e)
                     {
                         Log.Debug($"Could not compute backup stats for {zipPath}: {e.Message}");
                     }
                 });
    }

    private static BackupContentStats ComputeBackupContentStats(string zipPath)
    {
        var operators = 0;
        var connections = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(".t3", StringComparison.OrdinalIgnoreCase))
                continue;

            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();

            // Each symbol-child serializes exactly one "SymbolId" key, each connection one
            // "SourceParentOrChildId". Counting the quoted keys avoids parsing the whole document.
            operators += CountOccurrences(text, "\"SymbolId\"");
            connections += CountOccurrences(text, "\"SourceParentOrChildId\"");
        }

        return new BackupContentStats(operators, connections);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string FormatSize(long bytes)
    {
        const long mb = 1024 * 1024;
        return bytes >= mb
                   ? $"{bytes / (float)mb:0.0} MB"
                   : $"{bytes / 1024} KB";
    }

    private readonly record struct BackupContentStats(int Operators, int Connections);

    private static readonly Dictionary<string, (double LastUpdate, string Label)> _savedInfoLabels = new();
    private static readonly List<(string Label, AutoBackup.AutoBackup.BackupEntry Entry)> _backupMenuItems = new();
    private static readonly Dictionary<string, int> _backupSymbolCountsByPath = new();
    private static readonly ConcurrentDictionary<string, BackupContentStats> _backupContentStats = new();
    private static readonly HashSet<string> _contentStatsQueued = new();
    private static string? _backupMenuFolder;
    private static double _backupMenuUpdateTime;

    private static readonly int _revealInExplorerId = nameof(_revealInExplorerId).GetHashCode();
    private static readonly int _restoreFromBackupId = nameof(_restoreFromBackupId).GetHashCode();
    private static readonly int _unloadProjectId = nameof(_unloadProjectId).GetHashCode();
    private static readonly int _archiveProjectId = nameof(_archiveProjectId).GetHashCode();
    private static readonly int _reactivateProjectId = nameof(_reactivateProjectId).GetHashCode();
    private static readonly int _restoreBrokenFromBackupId = nameof(_restoreBrokenFromBackupId).GetHashCode();
    private static readonly int _revealBrokenInExplorerId = nameof(_revealBrokenInExplorerId).GetHashCode();
    private static readonly int _noBackupsFoundId = nameof(_noBackupsFoundId).GetHashCode();
    private static readonly int _backupItemBaseId = nameof(_backupItemBaseId).GetHashCode();
}