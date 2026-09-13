#nullable enable
namespace T3.Editor.UiModel.Selection;

internal enum SetupEntityKinds
{
    None,
    ReferenceImage,
    Surface,
    Prop,
    Output,
    Slice,
    ContentSource,
    Patch,

    /// <summary>A local display or stream sender — machine state, so it resolves against the machine config, not the setup.</summary>
    Plug,
}
