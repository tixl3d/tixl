#nullable enable
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Editor.App;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The editor's own window as a content source, so the UI can be mapped onto a display like any other content —
/// for tutorials and live sets, where mirroring a screen through the OS costs a lot of GPU time on Windows. It
/// has no op behind it: its source carries <see cref="SourceChildId"/> in place of a SymbolChildId, and a Player,
/// which never registers it, resolves that source to nothing.
/// </summary>
internal static class EditorUiContent
{
    /** Fixed, so every setup that shows the UI names the same source. */
    public static readonly Guid SourceChildId = new("1b8759e4-0bfc-470b-93f4-287029f1ca84");

    public const string DefaultName = "Editor UI";

    /// <summary>Hands the resolver the UI's frame. Installed once at startup, after the main window exists.</summary>
    public static void Register()
    {
        OutputContentResolver.RegisterHostSupplier(SourceChildId, new Supplier());
    }

    public static bool IsEditorUi(Guid symbolChildId) => symbolChildId == SourceChildId;

    /// <summary>Adds the editor UI to the setup's content, once.</summary>
    public static void AddSource(SetupEntitySelection selection, Setup setup)
    {
        if (setup.FindSourceByChildId(SourceChildId) != null)
            return;

        SetupUndo.RunUndoable("Add editor UI", setup, () =>
                                                       {
                                                           // Marked as renamed so its slices read "Editor UI.N" rather than "Slice N".
                                                           setup.ContentSources.Add(new ContentSource
                                                                                        {
                                                                                            SymbolChildId = SourceChildId,
                                                                                            Name = DefaultName,
                                                                                            IsRenamed = true,
                                                                                        });
                                                           selection.Select(SetupEntityKinds.ContentSource, SourceChildId);
                                                       });
    }

    /// <summary>
    /// The pixel aspect of a slice cut from the editor UI, as a width : height pair for a fitted patch. False
    /// for a slice of any other source.
    /// </summary>
    public static bool TryGetSliceAspect(Setup setup, Guid sliceId, out Vector2 aspectRatio)
    {
        aspectRatio = Vector2.One;
        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source == null || !IsEditorUi(source.SymbolChildId))
            return false;

        var windowSize = ProgramWindows.Main.Size;
        var uv = slice!.UvRect;
        var width = windowSize.Width * (uv.Z - uv.X);
        var height = windowSize.Height * (uv.W - uv.Y);
        if (!(width > 0) || !(height > 0))
            return false;

        aspectRatio = new Vector2(width, height);
        return true;
    }

    /// <summary>The last presented UI frame, as it is. It has no settings an editor could change.</summary>
    private sealed class Supplier : IContentSupplier
    {
        public Vector4 GetColor(EvaluationContext context) => Vector4.One;
        public Texture2D? GetContent(EvaluationContext context) => ProgramWindows.UiFrame;
        public bool GetUpdateEnabled(EvaluationContext context) => true;
        public IInputSlot? UpdateInput => null;
        public Int2 GetResolution(EvaluationContext context) => default;
        public IInputSlot? ResolutionInput => null;

        public void InvalidateContent()
        {
        }
    }
}
