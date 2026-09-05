#nullable enable
using T3.Core.DataTypes;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// One hue per entity kind, shared by the outliner items, its column headers and the connections: content
/// and slices carry the texture type's colour (they are textures), surfaces the "controlled" green, outputs
/// and their patches stay neutral until their own colour is decided.
/// </summary>
internal static class SetupColors
{
    public static Color ForKind(SetupEntitySelection.EntityKind kind)
    {
        return kind switch
                   {
                       SetupEntitySelection.EntityKind.ContentSource
                           or SetupEntitySelection.EntityKind.Slice => TypeUiRegistry.GetPropertiesForType(typeof(Texture2D)).Color,
                       SetupEntitySelection.EntityKind.Surface => UiColors.StatusControlled,
                       _ => UiColors.TextMuted,
                   };
    }
}
