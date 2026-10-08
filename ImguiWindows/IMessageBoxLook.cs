using System.Numerics;

namespace SilkWindows;

/// <summary>
/// How a host application draws message box content, so boxes read like the rest of it. Called inside the box's
/// ImGui frame; the host's fonts must be usable there (e.g. through a shared font atlas).
/// </summary>
public interface IMessageBoxLook
{
    /** Draws Markdown, wrapped to the available width. */
    public void DrawMarkdown(string markdown);

    public Vector2 GetButtonSize(string label);

    /** The primary button is the one the box suggests; the others are drawn as secondary. */
    public bool DrawButton(string label, bool isPrimary);
}
