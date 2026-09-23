#nullable enable
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Selection;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// One hue per entity kind (see <see cref="SetupEntityKindInfo.Color"/>), shared by the Board cards, the
/// outliner items, its column headers and the connections.
/// </summary>
internal static class SetupColors
{
    public static Color ForKind(SetupEntityKinds kind) => SetupEntityKindInfo.Of(kind).Color;

    /// <summary>A kind's hue as label text: lifted and desaturated the way operator labels are.</summary>
    public static Color LabelFor(SetupEntityKinds kind) => ColorVariations.OperatorLabel.Apply(ForKind(kind));
}
