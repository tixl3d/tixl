namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// UI wording that names a part of the operating system. Composed once, so menus drawn every frame don't allocate.
/// </summary>
internal static class PlatformNames
{
    internal static readonly string FileBrowser = OperatingSystem.IsWindows() ? "Explorer"
                                                  : OperatingSystem.IsMacOS() ? "Finder"
                                                  : "File Manager";

    /// <summary>
    /// The first menu of the menu bar. On macOS the system's own app menu already carries the app's name, so a
    /// second "TiXL" next to it would read as a duplicate; its contents (projects, saving) fit "File" there.
    /// </summary>
    internal static readonly string MainMenu = OperatingSystem.IsMacOS() ? "File" : "TiXL";

    internal static readonly string RevealInFileBrowser = "Reveal in " + FileBrowser;
    internal static readonly string OpenInFileBrowser = "Open in " + FileBrowser;
}
