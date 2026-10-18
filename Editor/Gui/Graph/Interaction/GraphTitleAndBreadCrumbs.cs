using System.Diagnostics;
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Graph.Interaction;

/// <summary>
/// Draws the breadcrumbs and the composition's name as one row. Every element reserves its space as an item and
/// draws its icon or text through the draw list, so all of them share the same vertical centre and text baseline.
/// </summary>
internal static class GraphTitleAndBreadCrumbs
{
    public static void Draw(ProjectView projectView)
    {
        if (projectView.InstView == null)
            return;

        ImGui.SetCursorScreenPos(ImGui.GetWindowPos() + new Vector2(2, 2) * T3Ui.UiScaleFactor);
        _rowTop = ImGui.GetCursorScreenPos().Y;
        _rowHeight = ImGui.GetFrameHeight();

        // Baseline of the normal font when its line box is centred in the row; other fonts align to it.
        _baselineY = _rowTop + MathF.Floor((_rowHeight - Fonts.FontNormal.FontSize) / 2) + Fonts.FontNormal.Ascent;

        if (DrawBreadCrumbItem("Projects", Icon.Hub, isFirstInRow: true))
            projectView.Close();

        DrawSeparator();
        DrawBreadCrumbs(projectView);
        DrawNameAndNamespace(projectView.InstView);
    }

    private static void DrawBreadCrumbs(ProjectView projectView)
    {
        Debug.Assert(projectView.InstView != null);

        var composition = projectView.InstView.Instance;
        if (composition.Parent == null)
            return;

        var compHash = composition.GetHashCode();
        if (compHash != _lastCompositionHash)
        {
            _lastCompositionHash = compHash;
            var op = composition;

            _parentInstances.Clear();
            while (op.Parent != null)
            {
                op = op.Parent;
                _parentInstances.Insert(0, op);
            }
        }

        var isProject = true;
        foreach (var p in _parentInstances)
        {
            var icon = isProject ? Icon.FolderOpen : Icon.None;
            if (DrawBreadCrumbItem(p.Symbol.Name, icon))
            {
                projectView.TrySetCompositionOpToParent();
            }

            DrawSeparator();
            isProject = false;
        }
    }

    private static bool DrawBreadCrumbItem(string label, Icon icon, bool isFirstInRow = false)
    {
        var paddingX = MathF.Round(4 * T3Ui.UiScaleFactor);
        var iconTextGap = MathF.Round(4 * T3Ui.UiScaleFactor);

        var hasIcon = icon != Icon.None;
        var iconSize = Vector2.Zero;
        if (hasIcon)
            Icons.GetGlyphDefinition(icon, out _, out iconSize);

        var labelWidth = ImGui.CalcTextSize(label).X;
        var width = paddingX * 2 + labelWidth + (hasIcon ? iconSize.X + iconTextGap : 0);

        if (!isFirstInRow)
            ImGui.SameLine();

        ImGui.PushID(label.GetHashCode());
        var clicked = ImGui.InvisibleButton("##parent", new Vector2(width, _rowHeight));
        ImGui.PopID();

        var itemRectMin = ImGui.GetItemRectMin();
        var drawList = ImGui.GetWindowDrawList();
        var isItemHovered = ImGui.IsItemHovered();
        var color = isItemHovered ? UiColors.ForegroundFull : UiColors.TextMuted;

        if (isItemHovered)
        {
            drawList.AddRect(itemRectMin, ImGui.GetItemRectMax(), UiColors.ForegroundFull.Fade(0.4f), 3 * T3Ui.UiScaleFactor);
        }

        var x = itemRectMin.X + paddingX;
        if (hasIcon)
        {
            Icons.DrawIconAtScreenPosition(icon, new Vector2(x, CenteredIconTop(iconSize)), drawList, color);
            x += iconSize.X + iconTextGap;
        }

        drawList.AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, new Vector2(x, TextTop(Fonts.FontNormal)), color, label);
        return clicked;
    }

    private static void DrawSeparator()
    {
        const Icon separatorIcon = Icon.ChevronRight;
        Icons.GetGlyphDefinition(separatorIcon, out _, out var iconSize);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(iconSize.X, _rowHeight));
        var pos = new Vector2(ImGui.GetItemRectMin().X, CenteredIconTop(iconSize));
        Icons.DrawIconAtScreenPosition(separatorIcon, pos, ImGui.GetWindowDrawList(), UiColors.TextMuted);
    }

    private static void DrawNameAndNamespace(ProjectView.InstanceView compositionOp)
    {
        var drawList = ImGui.GetWindowDrawList();
        var paddingX = MathF.Round(4 * T3Ui.UiScaleFactor);

        var name = compositionOp.Symbol.Name;
        var boldFont = Fonts.FontBold;
        ImGui.PushFont(boldFont);
        var nameWidth = ImGui.CalcTextSize(name).X;
        ImGui.PopFont();

        var isProject = compositionOp.Instance.Parent == null;
        var iconSize = Vector2.Zero;
        var iconTextGap = 0f;
        if (isProject)
        {
            Icons.GetGlyphDefinition(Icon.FolderOpen, out _, out iconSize);
            iconTextGap = MathF.Round(4 * T3Ui.UiScaleFactor);
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(paddingX * 2 + iconSize.X + iconTextGap + nameWidth, _rowHeight));
        var nameX = ImGui.GetItemRectMin().X + paddingX;
        if (isProject)
        {
            Icons.DrawIconAtScreenPosition(Icon.FolderOpen, new Vector2(nameX, CenteredIconTop(iconSize)), drawList, UiColors.ForegroundFull);
            nameX += iconSize.X + iconTextGap;
        }

        drawList.AddText(boldFont, boldFont.FontSize, new Vector2(nameX, TextTop(boldFont)), UiColors.ForegroundFull, name);

        if (isProject && ImGui.BeginItemTooltip())
        {
            ImGui.EndTooltip();
        }

        var normalFont = Fonts.FontNormal;
        var namespaceColor = UiColors.ForegroundFull.Fade(0.2f);
        var namespaceX = ImGui.GetItemRectMax().X + paddingX;
        var textTop = TextTop(normalFont);
        drawList.AddText(normalFont, normalFont.FontSize, new Vector2(namespaceX, textTop), namespaceColor, NamespaceSeparator);
        namespaceX += ImGui.CalcTextSize(NamespaceSeparator).X;
        drawList.AddText(normalFont, normalFont.FontSize, new Vector2(namespaceX, textTop), namespaceColor, compositionOp.Symbol.Namespace);
    }

    private static float CenteredIconTop(Vector2 iconSize)
    {
        return _rowTop + MathF.Floor((_rowHeight - iconSize.Y) / 2);
    }

    /** Top of a font's line box so that its baseline matches the normal font's. */
    private static float TextTop(ImFontPtr font)
    {
        return MathF.Round(_baselineY - font.Ascent-1);
    }

    private const string NamespaceSeparator = "- ";
    private static readonly List<Instance> _parentInstances = [];
    private static int _lastCompositionHash;
    private static float _rowTop;
    private static float _rowHeight;
    private static float _baselineY;
}
