#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;

namespace T3.Core.Output;

/// <summary>
/// Finds and loads a project's output setup from its <see cref="Setup.FolderName"/> folder. Shared by the
/// editor, which loads the folder it is editing, and the player, which loads the copy shipped beside an
/// exported project — so both pick the same setup out of the same folder by the same rules.
/// </summary>
public static class SetupFiles
{
    /// <summary>
    /// Reads the machine config and the setup it names, falling back to the first setup in the folder. Returns
    /// false when the folder holds no setup at all; <paramref name="machineConfig"/> is always usable.
    /// </summary>
    /// <param name="wasRepaired">The file needed fixing to be loadable — the caller decides whether to write it back.</param>
    public static bool TryLoad(string metaFolder, out Setup? setup, out MachineConfig machineConfig, out bool wasRepaired)
    {
        setup = null;
        wasRepaired = false;
        machineConfig = new MachineConfig();

        // The machine config remembers which setup this machine last had active, so it is read first.
        var machineConfigPath = Path.Combine(metaFolder, MachineConfig.FileName);
        if (File.Exists(machineConfigPath))
            MachineConfig.TryLoadFromFile(machineConfigPath, out machineConfig);

        if (!Directory.Exists(metaFolder))
            return false;

        var activeName = machineConfig.ActiveSetupName;
        if (activeName.Length > 0)
        {
            var preferred = FilePathFor(metaFolder, activeName);
            if (File.Exists(preferred))
                Setup.TryLoadFromFile(preferred, out setup, out wasRepaired);
        }

        // The remembered setup is gone, or none was ever recorded.
        if (setup == null)
        {
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(metaFolder, "*" + Setup.FileSuffix))
                {
                    if (Setup.TryLoadFromFile(filePath, out setup, out wasRepaired))
                        break;
                }
            }
            catch (Exception e)
            {
                // An unreadable folder must not take the host down with it: a player runs unattended, and an
                // editor can still open the project and write a fresh setup.
                Log.Warning($"Could not read output setups from \"{metaFolder}\": {e.Message}");
            }
        }

        return setup != null;
    }

    /// <summary>The path a setup of this name is stored at.</summary>
    public static string FilePathFor(string metaFolder, string setupName)
    {
        return Path.Combine(metaFolder, setupName + Setup.FileSuffix);
    }

    /// <summary>
    /// Fills each output's <see cref="OutputDefinition.ResolvedResolution"/>: its own canvas size, or the size of
    /// whatever is bound to it when that is left at 0×0. A binding is machine state, so this lives outside the
    /// model and the setup file stays free of display numbering. Fitted patches are re-derived right after, so an
    /// output of another aspect re-fits them before anything draws.
    /// </summary>
    /// <param name="resolutionOfBinding">Resolves a binding to the pixel size behind it. A host with no display
    /// inventory passes null, and every plug-following output falls back to <see cref="UnboundResolution"/>.</param>
    public static void ResolveCanvasResolutions(Setup setup, MachineConfig machineConfig,
                                                Func<PlugBinding?, Int2>? resolutionOfBinding)
    {
        foreach (var output in setup.Outputs)
        {
            if (!output.FollowsPlug)
            {
                output.ResolvedResolution = output.CanvasResolution;
                continue;
            }

            var binding = machineConfig.FindBinding(output.Id);
            var resolution = resolutionOfBinding == null ? UnboundResolution : resolutionOfBinding(binding);
            output.ResolvedResolution = resolution.Width > 0 && resolution.Height > 0 ? resolution : UnboundResolution;
        }

        foreach (var output in setup.Outputs)
        {
            var canvas = output.CanvasSize;
            foreach (var patch in output.Patches)
                patch.TryFitQuad(canvas);
        }
    }

    /// <summary>What an output that follows its plug renders at while nothing is bound to it.</summary>
    public static readonly Int2 UnboundResolution = new(1920, 1080);
}
