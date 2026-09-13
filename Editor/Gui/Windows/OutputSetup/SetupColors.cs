#nullable enable
using T3.Core.DataTypes;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// One hue per entity kind, shared by the Board cards, the outliner items, its column headers and the
/// connections: content and slices carry the texture type's colour (they are textures), surfaces and their
/// regions the physical green (the string type's, as a placeholder), reference images teal (the command
/// type's), outputs and their patches stay neutral. Selection never changes a hue — the outline gets wider.
/// </summary>
internal static class SetupColors
{
    public static Color ForKind(SetupEntityKinds kind)
    {
        return kind switch
                   {
                       SetupEntityKinds.ContentSource
                           or SetupEntityKinds.Slice => TypeUiRegistry.GetPropertiesForType(typeof(Texture2D)).Color,
                       SetupEntityKinds.Surface => TypeUiRegistry.GetPropertiesForType(typeof(string)).Color,
                       SetupEntityKinds.ReferenceImage => TypeUiRegistry.GetPropertiesForType(typeof(Command)).Color,
                       _ => UiColors.TextMuted,
                   };
    }

    /// <summary>A kind's hue as label text: lifted and desaturated the way operator labels are.</summary>
    public static Color LabelFor(SetupEntityKinds kind) => ColorVariations.OperatorLabel.Apply(ForKind(kind));
}
