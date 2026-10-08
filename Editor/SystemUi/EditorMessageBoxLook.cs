using SilkWindows;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;

namespace T3.Editor.SystemUi;

/// <summary>
/// Draws message boxes with the editor's Markdown and call-to-action buttons. Operator links show but don't
/// navigate: the box is modal, so the editor behind it can't follow them.
/// </summary>
internal sealed class EditorMessageBoxLook : IMessageBoxLook
{
    public void DrawMarkdown(string markdown) => _markdown.Draw(markdown);

    public Vector2 GetButtonSize(string label) => CustomComponents.GetCtaButtonSize(label);

    public bool DrawButton(string label, bool isPrimary)
    {
        return CustomComponents.DrawCtaButton(label, Icon.None,
                                              isPrimary ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default);
    }

    private readonly MarkdownView _markdown = new(new MarkdownView.Options());
}
