namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// UI wording that names a part of the operating system. Composed once, so menus drawn every frame don't allocate.
/// </summary>
internal static class PlatformNames
{
    internal static readonly string FileBrowser = OperatingSystem.IsWindows() ? "Explorer"
                                                  : OperatingSystem.IsMacOS() ? "Finder"
                                                  : "File Manager";

    internal static readonly string RevealInFileBrowser = "Reveal in " + FileBrowser;
    internal static readonly string OpenInFileBrowser = "Open in " + FileBrowser;
}
