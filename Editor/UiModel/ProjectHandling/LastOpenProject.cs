#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ImGuiNET;
using T3.Core.Model;
using T3.Core.Settings;
using T3.Editor.App.DebugProtocol;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// Remembers which project was open when the editor closed and reopens it on the next start.
/// </summary>
/// <remarks>
/// A project that crashes the editor while opening would otherwise crash every start. A marker file lives
/// through the first frames after reopening; finding it on the next start means that attempt failed, so the
/// editor starts in the project hub instead.
/// </remarks>
internal static class LastOpenProject
{
    private enum StartupStates
    {
        Undecided,
        WaitingForStartupLayout,
        ReadyToOpen,
        ApplyingProjectLayout,
        Done,
    }

    internal static void Remember(SymbolPackage package)
    {
        UserSettings.Config.LastOpenProjectFolder = package.Folder;
    }

    /** Called when the user leaves a project for the hub, so the next start shows the hub as well. */
    internal static void ForgetIfCurrent(SymbolPackage package)
    {
        if (IsSameFolder(UserSettings.Config.LastOpenProjectFolder, package.Folder))
            UserSettings.Config.LastOpenProjectFolder = string.Empty;
    }

    /// <summary>
    /// Called every frame from startup until it returns true. Covers the editor with a loading screen while the
    /// remembered project opens, because opening blocks the UI for a while.
    /// </summary>
    internal static bool ProcessStartupReopen()
    {
        switch (_startupState)
        {
            case StartupStates.Undecided:
                if (!ShouldReopen())
                {
                    _startupState = StartupStates.Done;
                    return true;
                }

                _startupState = StartupStates.WaitingForStartupLayout;
                break;

            // The project's window layout must not be loaded before ImGui has docked the windows of the startup
            // layout, otherwise they end up undocked and scattered.
            case StartupStates.WaitingForStartupLayout:
                if (ImGui.GetFrameCount() >= FirstFrameForReopen && string.IsNullOrEmpty(Program.NewImGuiLayoutDefinition))
                    _startupState = StartupStates.ReadyToOpen;

                break;

            // Opening only after a frame with the loading screen was presented, so it shows during the stall.
            case StartupStates.ReadyToOpen:
                TryReopen();
                _hideLoadingScreenAfterFrame = ImGui.GetFrameCount() + FramesForProjectLayoutToApply;
                _startupState = StartupStates.ApplyingProjectLayout;
                break;

            case StartupStates.ApplyingProjectLayout:
                if (ImGui.GetFrameCount() >= _hideLoadingScreenAfterFrame)
                {
                    _startupState = StartupStates.Done;
                    return true;
                }

                break;

            case StartupStates.Done:
                return true;
        }

        DrawLoadingScreen();
        return false;
    }

    private static bool ShouldReopen()
    {
        if (!UserSettings.Config.ReopenLastProjectOnStartup || string.IsNullOrEmpty(UserSettings.Config.LastOpenProjectFolder))
            return false;

        // Scripted sessions open their own projects and expect a predictable start.
        if (DebugServer.IsRunning)
            return false;

        if (File.Exists(ReopenGuardFilePath))
        {
            Log.Warning($"Reopening '{UserSettings.Config.LastOpenProjectFolder}' did not complete last time. Starting in the project hub instead.");
            ReleaseReopenGuard();
            return false;
        }

        return true;
    }

    private static void TryReopen()
    {
        var folder = UserSettings.Config.LastOpenProjectFolder;
        if (!TryFindPackage(folder, out var package))
        {
            Log.Debug($"Last open project '{folder}' is no longer available.");
            return;
        }

        if (!package.HasHome || package.CorruptedSymbolFilePaths.Count > 0)
            return;

        var graphWindow = FindGraphWindow();
        if (graphWindow == null)
            return;

        if (!OpenedProject.TryCreate(package, out var openedProject, out var failureLog))
        {
            Log.Warning($"Failed to reopen project '{package.DisplayName}': {failureLog}");
            return;
        }

        try
        {
            File.WriteAllText(ReopenGuardFilePath, folder);
        }
        catch (Exception e)
        {
            Log.Debug($"Can't write {ReopenGuardFilePath}: {e.Message}");
        }

        _releaseGuardAfterFrame = ImGui.GetFrameCount() + FramesUntilReopenSucceeded;
        graphWindow.TrySetToProject(openedProject);
    }

    private static void DrawLoadingScreen()
    {
        var drawList = ImGui.GetForegroundDrawList();
        var viewport = ImGui.GetMainViewport();
        drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, UiColors.BackgroundFull);

        var center = viewport.Pos + viewport.Size * 0.5f;
        var titleFont = Fonts.FontLarge;
        var titleSize = titleFont.CalcTextSizeA(titleFont.FontSize, float.MaxValue, 0, LoadingLabel);
        drawList.AddText(titleFont, titleFont.FontSize, center - titleSize * 0.5f, UiColors.Text, LoadingLabel);

        var hintFont = Fonts.FontSmall;
        var hintSize = hintFont.CalcTextSizeA(hintFont.FontSize, float.MaxValue, 0, SettingsHintLabel);
        var hintPos = new Vector2(center.X - hintSize.X * 0.5f, center.Y + titleSize.Y * 0.5f + 4 * T3Ui.UiScaleFactor);
        drawList.AddText(hintFont, hintFont.FontSize, hintPos, UiColors.TextMuted, SettingsHintLabel);
    }

    /** Called every frame; deletes the reopen guard once the reopened project has survived its first frames. */
    internal static void Update()
    {
        if (_releaseGuardAfterFrame == 0 || ImGui.GetFrameCount() < _releaseGuardAfterFrame)
            return;

        ReleaseReopenGuard();
    }

    internal static void ReleaseReopenGuard()
    {
        _releaseGuardAfterFrame = 0;
        try
        {
            File.Delete(ReopenGuardFilePath);
        }
        catch (Exception e)
        {
            Log.Debug($"Can't delete {ReopenGuardFilePath}: {e.Message}");
        }
    }

    private static bool TryFindPackage(string folder, [NotNullWhen(true)] out EditorSymbolPackage? package)
    {
        foreach (var candidate in SymbolPackage.AllPackages)
        {
            if (candidate is EditorSymbolPackage editorPackage && IsSameFolder(editorPackage.Folder, folder))
            {
                package = editorPackage;
                return true;
            }
        }

        package = null;
        return false;
    }

    /** Prefers the visible graph window, because hidden instances can exist while the hub is shown. */
    private static GraphWindow? FindGraphWindow()
    {
        foreach (var window in GraphWindow.GraphWindowInstances)
        {
            if (window.Config.Visible)
                return window;
        }

        return GraphWindow.GraphWindowInstances.Count > 0 ? GraphWindow.GraphWindowInstances[0] : null;
    }

    private static bool IsSameFolder(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                             Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                             comparison);
    }

    private static string ReopenGuardFilePath => Path.Combine(FileLocations.SettingsDirectory, "reopeningLastProject");

    private const string LoadingLabel = "Loading last session...";
    private const string SettingsHintLabel = "Can be disabled in Settings.";

    /** The startup layout is loaded on frame 3; a few more frames let its dock nodes build. */
    private const int FirstFrameForReopen = 8;

    /** The project's layout is loaded at the start of the next frame and docks in the one after. */
    private const int FramesForProjectLayoutToApply = 2;

    /** Long enough for the project's outputs to render once, which is where a broken project usually crashes. */
    private const int FramesUntilReopenSucceeded = 120;

    private static StartupStates _startupState;
    private static int _hideLoadingScreenAfterFrame;
    private static int _releaseGuardAfterFrame;
}
