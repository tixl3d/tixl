using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel;

public sealed class Section : ISelectableCanvasObject
{
    internal string Label = "";
    internal string Title = "";
    internal Color Color = UiColors.Gray;
    public Guid Id { get; internal init; }

    /// <summary>
    /// Optional parent section for nesting; sections form a tree per symbol graph.
    /// Guid.Empty means top level. Serialized.
    /// </summary>
    public Guid ParentSectionId { get; set; }

    public Vector2 PosOnCanvas { get; set; }
    public Vector2 Size { get; set; }
    public bool Collapsed = false;

    internal Section Clone()
    {
        return new Section
                   {
                       Id = Guid.NewGuid(),
                       Label = Label,
                       Title = Title,
                       Color = Color,
                       ParentSectionId = ParentSectionId,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size,
                       Collapsed = Collapsed,
                   };
    }
}
