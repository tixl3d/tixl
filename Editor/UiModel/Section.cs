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

    /// <summary>
    /// Runtime lookup: the outermost collapsed ancestor section this frame is hidden in,
    /// or Guid.Empty when visible. A collapsed section itself stays visible as its header.
    /// Recomputed by <see cref="SectionTree.UpdateCollapsedVisibility"/>; not serialized.
    /// </summary>
    internal Guid HiddenInCollapsedSectionId { get; set; }

    internal bool IsHiddenInCollapsedSection => HiddenInCollapsedSectionId != Guid.Empty;

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
