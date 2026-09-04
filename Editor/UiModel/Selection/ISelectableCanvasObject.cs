using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.UiModel.Selection;

public interface ISelectableCanvasObject
{
    Guid Id { get; }
    Vector2 PosOnCanvas { get; set; }
    Vector2 Size { get; set; }
    public Vector2 TopRightPosOnCanvas => PosOnCanvas + Size;
    public ImRect Rect
    {
        get
        {
            var pos = PosOnCanvas;
            return new ImRect(pos, pos + Size);
        }
    }
}

internal interface ISelectionContainer
{
    IEnumerable<ISelectableCanvasObject> GetSelectables();
}

public interface ISelection
{
    public bool IsNodeSelected(ISelectableCanvasObject obj);
}