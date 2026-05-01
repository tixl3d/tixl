#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Visual validation surface for <see cref="MarkdownView"/>. Hosted in the
/// Utilities window. Remove once the manual test runner adopts the renderer
/// as a real consumer.
/// </summary>
internal static class MarkdownPreview
{
    public static void Draw()
    {
        FormInputs.AddSectionHeader("Markdown preview");
        FormInputs.AddVerticalSpace(5);

        if (ImGui.Button("Reset to sample"))
            _sample = _defaultSample;

        ImGui.SameLine();
        ImGui.TextDisabled($"{_sample.Length} chars");

        if (ImGui.InputTextMultiline("##source", ref _sample, 8000,
                                      new Vector2(-1, 200 * T3Ui.UiScaleFactor)))
        {
            // _sample is a new string instance — the renderer's reference-equality
            // cache key invalidates automatically.
        }

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(8);

        ImGui.BeginChild("preview", new Vector2(-1, -1),
                         ImGuiChildFlags.Borders, ImGuiWindowFlags.None);
        _view.Draw(_sample,
                   url => Log.Info($"[MarkdownPreview] link clicked: {url}"),
                   onOperatorRef: HandleOperatorRefRendered);
        ImGui.EndChild();
    }

    private static void HandleOperatorRefRendered(string opName)
    {
        // The OpRef callback fires every frame for each rendered [OpName]
        // fragment. Only log on a real hover+click so the console doesn't
        // flood.
        if (!ImGui.IsItemHovered())
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            Log.Info($"[MarkdownPreview] op ref clicked: {opName}");
    }

    private static readonly MarkdownView _view = new(new MarkdownView.Options());

    private const string _defaultSample =
        """
        # Heading One
        Body text under H1. This paragraph wraps when it gets too long for the available width, and the wrapping should respect **bold runs** and `inline code` correctly.

        ## Heading Two
        Another paragraph. References to operators like [RadialGradient] should be styled and clickable. External links work too: [TiXL wiki](https://github.com/tixl3d/tixl/wiki).

        ### Heading Three
        - bullet at depth 0
        - second bullet with **bold** and `code` inline
          - nested bullet at depth 1
          - second nested
            - depth 2
        - back to depth 0

        ## Numbered list
        1. first item
        2. second item with a [link](https://example.com)
        3. third item, much longer text that should wrap onto a second visual line and use the hanging indent so it stays aligned under the content of the first line
           1. nested numbered
           2. another nested
        """;

    private static string _sample = _defaultSample;
}
