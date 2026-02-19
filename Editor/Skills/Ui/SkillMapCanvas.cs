#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills.Data;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Skills.Ui;

internal sealed class SkillMapCanvas : HexCanvas
{
    public bool DrawContent(HandleTopicInteraction? topicAction, out HexCanvas.Cell mouseCell, HashSet<QuestTopic> selection)
    {
        if (!ImGui.IsWindowAppearing())
        {
            if (_focusCanvasAreaRequested)
            {
                FitAreaOnCanvas(_focusAreaOnCanvas);
                _focusCanvasAreaRequested = false;
            }
        }
        UpdateCanvas(out _);

        var dl = ImGui.GetWindowDrawList();

        var mousePos = ImGui.GetMousePos();
        mouseCell = CellFromScreenPos(mousePos);

        var isAnyItemHovered = false;
        foreach (var topic in SkillMapData.Data.Topics)
        {
            isAnyItemHovered |= DrawTopicCell(dl, topic, mouseCell, selection, topicAction);
        }

        return isAnyItemHovered;
    }

    private static readonly HashSet<QuestTopic> _noSelection = []; 

    public void FocusTopics(HashSet<QuestTopic> selection, float padding = 160)
    {
        if (selection.Count == 0)
            return;

        var isFirst = true;
        ImRect canvasArea = new ImRect();
        foreach (var topic in selection)
        {
            var posOnCanvas = CanvasPosFromCell(topic.Cell);
            if (isFirst)
            {
                canvasArea = ImRect.RectWithSize(posOnCanvas, Vector2.One);
                isFirst = false;
            }
            else
            {
                canvasArea.Add(posOnCanvas);
            }
        }

        canvasArea.Expand(padding);
        _focusAreaOnCanvas = canvasArea;
        _focusCanvasAreaRequested = true;
    }

    private bool _focusCanvasAreaRequested;
    private ImRect _focusAreaOnCanvas;

    /// <returns>
    /// return true if hovered
    /// </returns>
    private bool DrawTopicCell(ImDrawListPtr dl, QuestTopic topic, HexCanvas.Cell cellUnderMouse, HashSet<QuestTopic> selection,
                               HandleTopicInteraction? topicAction)
    {
        var cell = new HexCanvas.Cell(topic.MapCoordinate);

        var isHovered = ImGui.IsWindowHovered() && !ImGui.IsMouseDown(ImGuiMouseButton.Right) && cell == cellUnderMouse;

        var posOnScreen = MapCoordsToScreenPos(topic.MapCoordinate);
        var radius = HexRadiusOnScreen;

        GetTopicColorAndStateFade(topic, out var typeColor, out var unlockProgressFade);
        var isHoveredFade = isHovered ? 1f : 0.9f;
        dl.AddNgonRotated(posOnScreen, radius * 0.95f, typeColor.Fade(isHoveredFade * unlockProgressFade));

        var isSelected = selection.Contains(topic);
        if (isSelected)
        {
            dl.AddNgonRotated(posOnScreen, radius, UiColors.ForegroundFull, false);
        }

        foreach (var unlockTargetId in topic.UnlocksTopics)
        {
            if (!SkillMapData.TryGetTopic(unlockTargetId, out var targetTopic))
                continue;

            
            
            var targetPos = MapCoordsToScreenPos(targetTopic.MapCoordinate);
            var isLocked = targetTopic.ProgressionState is QuestTopic.ProgressStates.Locked or QuestTopic.ProgressStates.Upcoming;
            var lockedFade = isLocked ? 0.5f : 1;
            var delta = posOnScreen - targetPos;
            var direction = Vector2.Normalize(delta);
            var angle = -MathF.Atan2(delta.X, delta.Y) - MathF.PI / 2;
            var fadeLine = (delta.Length() / Scale.X).RemapAndClamp(0f, 2000f, 1, 0.06f) * lockedFade;

            dl.AddLine(posOnScreen - direction * radius * 0.83f,
                       targetPos + direction * radius * 0.83f,
                       typeColor.Fade(fadeLine),
                       2);
            
            dl.AddNgonRotated(targetPos + direction * radius * 0.83f,
                              15 * Scale.X,
                              typeColor.Fade(fadeLine),
                              true,
                              3,
                              startAngle: angle);
        }

        if (!string.IsNullOrEmpty(topic.Title))
        {
            var labelAlpha = Scale.X.RemapAndClamp(0.25f, 0.6f, 0, 1);
            if (labelAlpha > 0.01f)
            {
                ImGui.PushFont(Scale.X < 0.55f ? Fonts.FontSmall : Fonts.FontNormal);
                CustomImguiDraw.AddWrappedCenteredText(dl, 
                                                       topic.Title, 
                                                       posOnScreen - new Vector2(0, 10f * Scale.Y), 
                                                       12, 
                                                       UiColors.ForegroundFull.Fade(labelAlpha),
                                                       verticalAlign:0.7f);
                ImGui.PopFont();

                if (
                    topic.ProgressionState == QuestTopic.ProgressStates.Upcoming
                   )
                {
                    Icons.DrawIconAtScreenPosition(Icon.Locked, (posOnScreen + new Vector2(-Icons.FontSize / 2, 25f * Scale.Y)).Floor(),
                                                   dl,
                                                   UiColors.ForegroundFull.Fade(0.4f * labelAlpha));
                }
                // else if (topic.ProgressionState == QuestTopic.ProgressStates.Locked)
                // {
                //     Icons.DrawIconAtScreenPosition(Icon.Locked, (posOnScreen + new Vector2(-Icons.FontSize / 2, 25f * Scale.Y)).Floor(),
                //                                    dl,
                //                                    UiColors.ForegroundFull.Fade(0.4f * labelAlpha));
                // }
                else if (topic.ProgressionState == QuestTopic.ProgressStates.Completed)
                {
                    Icons.DrawIconAtScreenPosition(Icon.Checkmark, (posOnScreen + new Vector2(-Icons.FontSize / 2, 25f * Scale.Y)).Floor(),
                                                   dl,
                                                   UiColors.ForegroundFull.Fade(0.4f * labelAlpha));
                }
                else
                {
                    ImGui.PushFont(Scale.X < 0.6f ? Fonts.FontSmall : Fonts.FontNormal);
                    CustomImguiDraw.AddWrappedCenteredText(dl,
                                                           $"{topic.CompletedLevelCount} / {topic.Levels.Count}",
                                                           posOnScreen + new Vector2(0, 25f * Scale.Y),
                                                           13, UiColors.ForegroundFull.Fade(labelAlpha));
                    ImGui.PopFont();
                }
            }
        }

        if (!isHovered)
            return isHovered;

        // Mouse interactions ----------------

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3));
        ImGui.BeginTooltip();
        {
            {
                CustomComponents.StylizedText(topic.ProgressionState.ToString(), Fonts.FontSmall, UiColors.TextMuted);
            }
            
            ImGui.TextUnformatted(topic.Title);
            if (!string.IsNullOrEmpty(topic.Description))
            {
                CustomComponents.StylizedText(topic.Description, Fonts.FontSmall, UiColors.TextMuted);
            }
            
        }
        ImGui.EndTooltip();
        ImGui.PopStyleVar();

        topicAction?.Invoke(topic, isSelected);
        return isHovered;
    }

    internal static void GetTopicColorAndStateFade(QuestTopic topic, out Color typeColor, out float unlockProgressFade)
    {
        var type = topic.TopicType switch
                       {
                           QuestTopic.TopicTypes.Image       => typeof(Texture2D),
                           QuestTopic.TopicTypes.Numbers     => typeof(float),
                           QuestTopic.TopicTypes.Command     => typeof(Command),
                           QuestTopic.TopicTypes.String      => typeof(string),
                           QuestTopic.TopicTypes.Gpu         => typeof(BufferWithViews),
                           QuestTopic.TopicTypes.ShaderGraph => typeof(ShaderGraphNode),
                           _                                 => throw new ArgumentOutOfRangeException()
                       };
        typeColor = TypeUiRegistry.GetTypeOrDefaultColor(type);

        unlockProgressFade = topic.ProgressionState switch
                                     {
                                         QuestTopic.ProgressStates.Upcoming  => 0.2f,
                                         QuestTopic.ProgressStates.Locked    => 0.5f,
                                         QuestTopic.ProgressStates.Completed => 0.8f,
                                         QuestTopic.ProgressStates.Active    => 0.8f,
                                         _                                   => 1f
                                     };
    }

    internal delegate void HandleTopicInteraction(QuestTopic topic, bool isSelected);
}