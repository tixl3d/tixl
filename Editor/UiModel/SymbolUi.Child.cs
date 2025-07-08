using T3.Core.Operator;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel;

public partial class SymbolUi
{
    /// <summary>
    /// Properties needed for visual representation of an instance. Should later be moved to gui component.
    /// </summary>
    public sealed class Child : ISelectableCanvasObject
    {
        internal enum Styles
        {
            Default,
            Expanded,
            Resizable,
            WithThumbnail,
        }

        internal enum ConnectionStyles
        {
            Default,
            FadedOut,
        }

        internal static Vector2 DefaultOpSize { get; } = new(110, 25);

        internal Dictionary<Guid, ConnectionStyles> ConnectionStyleOverrides { get; } = new();

        internal Symbol.Child SymbolChild => Parent.Children.GetValueOrDefault(Id);
        private Symbol Parent => _parentSymbolPackage.Symbols[_symbolId];

        private readonly Guid _symbolId;
        private EditorSymbolPackage _parentSymbolPackage;

        public Guid Id { get; }
        public Vector2 PosOnCanvas { get; set; } = Vector2.Zero;
        public Vector2 Size { get; set; } = DefaultOpSize;

        /// <summary>
        /// We use this index as a hack to distinuish the following states:
        /// 0 - not relevant for snapshots
        /// 1 - used for snapshots (creating a new snapshot will copy the current state of child)
        /// >1 - used for ParameterCollections 
        /// </summary>
        internal int SnapshotGroupIndex { get; set; }
        private const int GroupIndexForSnapshots = 1; 

        public bool EnabledForSnapshots
        {
            get => GroupIndexForSnapshots == SnapshotGroupIndex;
            set => SnapshotGroupIndex = value ? GroupIndexForSnapshots : 0;
        }            
            
        internal Styles Style;
        internal string Comment;

        //internal bool IsDisabled { get => SymbolChild.Outputs.FirstOrDefault().Value?.IsDisabled ?? false; set => SetDisabled(value); }

        internal Child(Guid symbolChildId, Guid symbolId, EditorSymbolPackage parentSymbolPackage)
        {
            Id = symbolChildId;
            _symbolId = symbolId;
            _parentSymbolPackage = parentSymbolPackage;
        }

        internal static Child CreateCopy(Child original, Guid symbolChildId, Guid symbolId, EditorSymbolPackage parentSymbolPackage)
        {
            var newChild = new Child(symbolChildId, symbolId, parentSymbolPackage)
            {
                PosOnCanvas = original.PosOnCanvas,
                Size = original.Size,
                Style = original.Style,
                Comment = original.Comment,
                SnapshotGroupIndex = original.SnapshotGroupIndex,
            };
            foreach(var overridePair in original.ConnectionStyleOverrides)
            {
                newChild.ConnectionStyleOverrides[overridePair.Key] = overridePair.Value;
            }
            
            return newChild;
        }

        internal void UpdateSymbolPackage(EditorSymbolPackage parentSymbolPackage)
        {
            _parentSymbolPackage = parentSymbolPackage;
        }



        /// <summary>
        /// 
        /// </summary>
        [Flags]
        public enum CustomUiResult
        {
            None = 0,
            Rendered = 1 << 2,
            IsActive = 1 << 3,
            PreventTooltip = 1 << 4,
            PreventOpenSubGraph = 1 << 5,
            PreventOpenParameterPopUp = 1 << 6,
            PreventInputLabels = 1 << 7,
        }

        internal Child Clone(SymbolUi parent, Symbol.Child symbolChild)
        {
            return new Child(symbolChild.Id, parent._id, (EditorSymbolPackage)parent.Symbol.SymbolPackage)
                       {
                           PosOnCanvas = PosOnCanvas,
                           Size = Size,
                           Style = Style,
                           Comment = Comment
                       };
        }

        public override string ToString()
        {
            return $"{SymbolChild.Parent.Name}>[{SymbolChild.ReadableName}]";
        }
    }
}