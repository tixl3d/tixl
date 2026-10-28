#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Model;
using T3.Core.Settings;
using T3.Editor.Gui.Windows.Layouts;
using T3.Serialization;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// How the user left a project: open composition, selection, navigation history, window layout and focus mode.
/// It lives in the project's untracked .temp folder, so restoring a session never marks the project as modified
/// and sharing or committing a project doesn't carry it along.
/// </summary>
/// <remarks>
/// Instance paths are stored as Guids joined by '/', because lists of Guids don't deserialize reliably.
/// </remarks>
internal sealed class ProjectSession
{
    public int Version = CurrentVersion;
    public string? CompositionPath;
    public List<string> SelectedChildIds = [];
    public List<string> NavigationHistory = [];

    public string? WindowLayout;
    public string? WindowLayoutImGuiVersion;
    public Dictionary<string, bool>? WindowVisibility;

    /** Set while in focus mode: the ui state to return to when leaving it. */
    public UiConfig.UiState? UiStateBeforeFocusMode;

    /** The operator shown as graph background in focus mode. */
    public string? BackgroundOutputPath;

    /** Optional fields can be added without bumping this; bump only for shape changes old readers would misread. */
    private const int CurrentVersion = 1;

    internal const int MaxNavigationHistoryCount = 100;

    internal static bool TryLoad(SymbolPackage package, [NotNullWhen(true)] out ProjectSession? session)
    {
        var filePath = GetFilePath(package);
        if (!JsonUtils.TryLoadingJson(filePath, out session))
            return false;

        if (session.Version > CurrentVersion)
        {
            Log.Debug($"Ignoring session {filePath} written by a newer TiXL version.");
            session = null;
            return false;
        }

        return true;
    }

    internal void Save(SymbolPackage package)
    {
        var filePath = GetFilePath(package);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        }
        catch (Exception e)
        {
            Log.Debug($"Can't create folder for session {filePath}: {e.Message}");
            return;
        }

        JsonUtils.TrySaveJson(this, filePath);
    }

    internal static string FormatPath(IReadOnlyList<Guid> path) => string.Join('/', path);

    internal static bool TryParsePath(string? text, [NotNullWhen(true)] out List<Guid>? path)
    {
        path = null;
        if (string.IsNullOrEmpty(text))
            return false;

        var parts = text.Split('/');
        var ids = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id))
                return false;

            ids.Add(id);
        }

        path = ids;
        return true;
    }

    /** Read-only packages (e.g. an installed library) can't be written to, so their sessions go to the settings folder. */
    private static string GetFilePath(SymbolPackage package)
    {
        return package.IsReadOnly
                   ? Path.Combine(FileLocations.SettingsDirectory, SessionsSubFolder, package.Id + ".json")
                   : Path.Combine(package.Folder, FileLocations.TempSubfolder, SessionFileName);
    }

    private const string SessionFileName = "EditorSession.json";
    private const string SessionsSubFolder = "ProjectSessions";
}
