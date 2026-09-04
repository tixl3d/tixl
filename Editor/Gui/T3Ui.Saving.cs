using System.Diagnostics;
using System.Threading.Tasks;
using T3.Editor.Compilation;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui;

public static partial class T3Ui
{
    internal static void SaveInBackground(bool saveAll)
    {
        // Refresh the layout snapshot here on the UI thread — Save() may run on a
        // background task and must not call into ImGui.
        ProjectView.SnapshotWindowLayoutForFocusedProject();
        Task.Run(() => Save(saveAll));
    }

    internal static void Save(bool saveAll)
    {
        if (_saveStopwatch.IsRunning)
        {
            Log.Debug("Can't save modified while saving is in progress");
            return;
        }

        _saveStopwatch.Restart();

        lock (ProjectSetup.SymbolDataLock)
        {
            // Todo - parallelize?
            foreach (var package in EditableSymbolProject.AllProjects)
            {
                if (saveAll)
                    package.SaveAll();
                else
                    package.SaveModifiedSymbols();
            }
        }

        _saveStopwatch.Stop();
    }
}

public static partial class T3Ui
{
    private static readonly Stopwatch _saveStopwatch = new();
    internal static bool IsCurrentlySaving => _saveStopwatch is { IsRunning: true };
}