#nullable enable
using System.IO;
using T3.Core.Settings;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Interaction.Variations.Model;

/// <summary>
/// Moves variation files from the pre-4.2 locations into the owning project's
/// .meta/Variations folder. Two legacy folders are swept: the per-user folder in AppData
/// and the shared defaults folder (in dev builds that is the repo's .Defaults, so moved
/// files show up as working-tree changes to commit). Files for symbols of read-only
/// packages stay where they are (the AppData folder remains the writable user overlay),
/// and files for unknown symbols are kept so the migration can retry once their project loads.
/// </summary>
internal static class VariationsMigration
{
    /// <summary>Requires all symbol packages to be loaded so symbol ids can be resolved.</summary>
    internal static void MigrateLegacyVariationsToPackageMeta()
    {
        MigrateFolder(Path.Combine(FileLocations.SettingsDirectory, SymbolVariationPool.UserOverlaySubFolder));
        MigrateFolder(Path.Combine(FileLocations.DefaultsSourcePath, SymbolVariationPool.UserOverlaySubFolder));
    }

    private static void MigrateFolder(string legacyFolder)
    {
        if (!Directory.Exists(legacyFolder))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(legacyFolder, "*.var");
        }
        catch (Exception e)
        {
            Log.Warning($"Skipping variations migration - failed to list {legacyFolder}: {e.Message}");
            return;
        }

        var movedCount = 0;
        foreach (var filePath in files)
        {
            try
            {
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(filePath), out var symbolId))
                    continue;

                if (!SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
                    continue;

                var package = symbolUi.Symbol.SymbolPackage;
                if (package.IsReadOnly)
                    continue;

                var targetPath = SymbolVariationPool.GetVariationFilePathInPackage(package.Folder, symbolId);
                if (File.Exists(targetPath))
                {
                    MergeIntoTarget(filePath, targetPath, symbolId);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Move(filePath, targetPath);
                }

                movedCount++;
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to migrate variations file {filePath}: {e.Message}");
            }
        }

        if (movedCount > 0)
            Log.Info($"Moved {movedCount} variation file(s) from {legacyFolder} into project .meta folders.");
    }

    /// <summary>Union by variation id; existing target entries win.</summary>
    private static void MergeIntoTarget(string sourcePath, string targetPath, Guid symbolId)
    {
        var target = SymbolVariationPool.LoadVariationsFromFile(targetPath, symbolId);
        var source = SymbolVariationPool.LoadVariationsFromFile(sourcePath, symbolId);

        var knownIds = new HashSet<Guid>();
        foreach (var variation in target)
        {
            knownIds.Add(variation.Id);
        }

        var addedCount = 0;
        foreach (var variation in source)
        {
            if (!knownIds.Add(variation.Id))
                continue;

            target.Add(variation);
            addedCount++;
        }

        if (addedCount > 0 && !SymbolVariationPool.TrySaveVariationsToFile(targetPath, symbolId, target))
            return; // Keep the source file so nothing is lost

        File.Delete(sourcePath);
    }
}
