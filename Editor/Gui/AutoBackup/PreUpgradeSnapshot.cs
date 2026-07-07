#nullable enable
using T3.Core.Model;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.AutoBackup;

/// <summary>
/// On the first launch of a TiXL version that introduces a breaking project-file format, snapshots
/// each existing project into a pinned backup so the user can return to an older build without losing
/// work. The pinned zip is never pruned and doubles as the "migrate back" path — reverting to it
/// restores the pre-upgrade files.
/// </summary>
internal static class PreUpgradeSnapshot
{
    /// <summary>One snapshot taken this launch.</summary>
    internal readonly record struct Entry(string ProjectName, string ZipPath);

    /// <summary>Snapshots created on this launch, for the welcome window to surface.</summary>
    public static IReadOnlyList<Entry> Created => _created;

    /// <summary>
    /// Snapshots every home project that lacks the boundary backup. Call once, only on a NewToUser
    /// launch, before the first save — on-disk files stay in the old format until a save rewrites them.
    /// Runs synchronously so a crash mid-pass leaves the version marker unwritten and the pass resumes
    /// next launch (already-snapshotted projects are skipped by the per-tag guard).
    /// </summary>
    public static void CreateForExistingProjects()
    {
        foreach (var project in EditableSymbolProject.AllProjects)
        {
            if (!project.HasHome)
                continue;

            try
            {
                if (AutoBackup.CreatePinnedBackup(project.Folder, BoundaryTag, out var zipPath) && zipPath != null)
                    _created.Add(new Entry(project.DisplayName, zipPath));
            }
            catch (Exception e)
            {
                Log.Warning($"Pre-upgrade backup of '{project.DisplayName}' failed: {e.Message}");
            }
        }

        if (_created.Count > 0)
            Log.Info($"Created {_created.Count} pre-upgrade project backup(s) before format {SymbolFormatVersion.Current}.");
    }

    // Tag keyed to the file-format version, not the editor version: a dev must bump
    // SymbolFormatVersion.Current on any breaking format change, so this snapshots exactly once per
    // format era per project — and can't be forgotten on future releases. The per-tag guard in
    // AutoBackup.CreatePinnedBackup keeps it one-time.
    private static string BoundaryTag => $"preFormat{SymbolFormatVersion.Current}";

    private static readonly List<Entry> _created = new();
}
