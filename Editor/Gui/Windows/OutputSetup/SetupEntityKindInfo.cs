#nullable enable
using T3.Core.DataTypes;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Selection;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The static facts about one <see cref="SetupEntityKinds"/>: how it looks and what the generic verbs may do
/// to it. Kept in one table so the outliner, the parameter header, the routing and the menus can never
/// disagree about a kind. Per-kind <i>behaviour</i> (delete cascades, cards, menu bodies) stays with its feature.
/// </summary>
internal sealed class SetupEntityKindInfo
{
    public readonly SetupEntityKinds Kind;
    public readonly Icon Icon;

    /// <summary>The singular display word — the parameter header, and the fallback where an entity has no name.</summary>
    public readonly string Label;

    /// <summary>Position along the content flow (source → slice → surface → output → plug); -1 for kinds
    /// outside it. Normalizes the direction of a drop.</summary>
    public readonly int RoutingRank;

    /// <summary>Both a drag source and a drop target for connections.</summary>
    public readonly bool IsRoutable;

    public readonly bool CanRename;

    /// <summary>Deletes here rather than through the graph (a content source is its op).</summary>
    public readonly bool CanDelete;

    public readonly bool CanDuplicate;

    /// <summary>
    /// The kind's hue, resolved per call so a theme change shows at once: content and slices carry the texture
    /// type's colour (they are textures), surfaces and their regions the physical green, reference images teal,
    /// outputs and their patches stay neutral. Selection never changes a hue — the outline gets wider.
    /// </summary>
    public Color Color => Kind switch
                              {
                                  SetupEntityKinds.ContentSource
                                      or SetupEntityKinds.Slice => TypeUiRegistry.GetPropertiesForType(typeof(Texture2D)).Color,
                                  SetupEntityKinds.Surface
                                      or SetupEntityKinds.FloorPlan => UiColors.SetupSurface,
                                  SetupEntityKinds.ReferenceImage => UiColors.SetupReferenceImage,
                                  _ => UiColors.TextMuted,
                              };

    public static SetupEntityKindInfo Of(SetupEntityKinds kind)
    {
        var index = (int)kind;
        return index >= 0 && index < _table.Length ? _table[index] : _table[0];
    }

    private SetupEntityKindInfo(SetupEntityKinds kind, Icon icon, string label, int routingRank,
                                bool isRoutable, bool canRename, bool canDelete, bool canDuplicate)
    {
        Kind = kind;
        Icon = icon;
        Label = label;
        RoutingRank = routingRank;
        IsRoutable = isRoutable;
        CanRename = canRename;
        CanDelete = canDelete;
        CanDuplicate = canDuplicate;
    }

    /** Indexed by the enum's value; every SetupEntityKinds member has exactly one entry. */
    private static readonly SetupEntityKindInfo[] _table = BuildTable();

    private static SetupEntityKindInfo[] BuildTable()
    {
        var table = new SetupEntityKindInfo[Enum.GetValues<SetupEntityKinds>().Length];
        Add(new SetupEntityKindInfo(SetupEntityKinds.None, Icon.Grid, "None", -1, false, false, false, false));
        Add(new SetupEntityKindInfo(SetupEntityKinds.ReferenceImage, Icon.FileImage, "Reference Image", -1, false, true, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.Surface, Icon.Grid, "Surface", 2, true, true, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.Prop, Icon.Grid, "Prop", -1, false, false, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.FloorPlan, Icon.Mapping, "Floor Plan", -1, false, true, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.Output, Icon.Projector, "Output", 3, true, true, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.Slice, Icon.Slice, "Slice", 1, true, true, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.ContentSource, Icon.FileImage, "Content", 0, true, true, false, false));
        Add(new SetupEntityKindInfo(SetupEntityKinds.Patch, Icon.Patch, "Patch", 3, true, true, true, true));
        Add(new SetupEntityKindInfo(SetupEntityKinds.Plug, Icon.PlayOutput, "Plug", 4, true, true, false, false));
        return table;

        void Add(SetupEntityKindInfo info) => table[(int)info.Kind] = info;
    }
}
