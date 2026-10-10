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

        /// <summary>
        /// Identifies one input connection of a child: the output it comes from and the input it feeds.
        /// A child's input has a single source slot, so together they are unique within the child.
        /// </summary>
        internal readonly record struct ConnectionTarget(Guid SourceOutputId, Guid TargetInputId);

        /// <summary>
        /// Returns the manual bend points of a connection, or null when it still follows its automatic route.
        /// </summary>
        internal List<Vector2>? GetConnectionWaypoints(ConnectionTarget target)
        {
            return ConnectionWaypoints.GetValueOrDefault(target);
        }

        /// <summary>Replaces a connection's bend points; an empty list removes the reroute entirely.</summary>
        internal void SetConnectionWaypoints(ConnectionTarget target, List<Vector2> waypoints)
        {
            if (waypoints.Count == 0)
            {
                ConnectionWaypoints.Remove(target);
                return;
            }

            ConnectionWaypoints[target] = [..waypoints];
        }

        /// <summary>
        /// Takes ownership of <paramref name="waypoints"/> instead of copying it. Callers must not reuse
        /// the list; this exists for drag operations, which would otherwise allocate a list every frame.
        /// </summary>
        internal void SetConnectionWaypointsOwned(ConnectionTarget target, List<Vector2> waypoints)
        {
            if (waypoints.Count == 0)
            {
                ConnectionWaypoints.Remove(target);
                return;
            }

            ConnectionWaypoints[target] = waypoints;
        }

        internal void RemoveConnectionWaypoints(ConnectionTarget target)
        {
            ConnectionWaypoints.Remove(target);
        }

        /// <summary>
        /// Moves every bend point of this child by <paramref name="offset"/>. Bend points are in canvas
        /// space, so a copy of a child that is placed somewhere else needs its routes moved with it.
        /// </summary>
        internal void OffsetConnectionWaypoints(Vector2 offset)
        {
            if (ConnectionWaypoints.Count == 0)
                return;

            foreach (var (target, waypoints) in ConnectionWaypoints)
            {
                for (var index = 0; index < waypoints.Count; index++)
                {
                    waypoints[index] += offset;
                }
            }
        }

        internal static Vector2 DefaultOpSize { get; } = new(110, 25);

        internal Dictionary<Guid, ConnectionStyles> ConnectionStyleOverrides { get; } = new();

        /// <summary>
        /// Manual bend points ("waypoints") that reroute individual input connections around the nodes
        /// they would otherwise cross, in canvas space.
        /// </summary>
        /// <remarks>
        /// Purely cosmetic: the connection path has no effect on evaluation. A child's input has one
        /// source, so the target input id identifies the connection.
        /// </remarks>
        internal Dictionary<ConnectionTarget, List<Vector2>> ConnectionWaypoints { get; } = new();

        internal Symbol.Child SymbolChild => Parent.Children.GetValueOrDefault(Id);
        private Symbol Parent => _parentSymbolPackage.Symbols[_symbolId];

        private readonly Guid _symbolId;
        private EditorSymbolPackage _parentSymbolPackage;

        public Guid Id { get; }
        public Vector2 PosOnCanvas { get; set; } = Vector2.Zero;
        public Vector2 Size { get; set; } = DefaultOpSize;

        /// <summary>
        /// The section this op belongs to; Guid.Empty when unsectioned. Explicit serialized
        /// membership — changed only through discrete, undoable user actions, never derived
        /// per frame from geometry.
        /// </summary>
        public Guid SectionId { get; set; }

        /// <summary>
        /// Runtime lookup: the outermost collapsed section this op is hidden in, or Guid.Empty
        /// when visible. Recomputed by <see cref="SectionTree.UpdateCollapsedVisibility"/> on
        /// structural changes; not serialized.
        /// </summary>
        internal Guid HiddenInCollapsedSectionId { get; set; }

        public bool IsHiddenInCollapsedSection => HiddenInCollapsedSectionId != Guid.Empty;


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

        /// <summary>
        /// Input ids enabled for snapshot control while <see cref="EnabledForSnapshots"/> is set.
        /// Null means all blendable inputs are enabled — the legacy per-op semantics. The set is
        /// only materialized once the user toggles an individual parameter, so untouched files
        /// keep their original shape.
        /// </summary>
        internal HashSet<Guid>? SnapshotEnabledInputIds { get; set; }

        /// <summary>
        /// True if the parameter is controlled by snapshots — shown with the controller indicator
        /// and listed in the snapshot control view.
        /// </summary>
        internal bool IsInputEnabledForSnapshots(Guid inputId)
        {
            return EnabledForSnapshots
                   && (SnapshotEnabledInputIds == null || SnapshotEnabledInputIds.Contains(inputId));
        }

        /// <summary>
        /// Filter for variation capture and apply paths that already qualified the child:
        /// ParameterCollections (group index above 1) always include everything.
        /// </summary>
        internal bool IsInputIncludedForVariation(Guid inputId)
        {
            if (SnapshotGroupIndex != GroupIndexForSnapshots || SnapshotEnabledInputIds == null)
                return true;

            return SnapshotEnabledInputIds.Contains(inputId);
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
                SectionId = original.SectionId, // Careful! When duplicating the parent symbol or section, this needs to be updated.
                Style = original.Style,
                Comment = original.Comment,
                SnapshotGroupIndex = original.SnapshotGroupIndex,
                SnapshotEnabledInputIds = original.SnapshotEnabledInputIds == null ? null : [..original.SnapshotEnabledInputIds],
            };
            foreach(var overridePair in original.ConnectionStyleOverrides)
            {
                newChild.ConnectionStyleOverrides[overridePair.Key] = overridePair.Value;
            }

            foreach (var waypoints in original.ConnectionWaypoints)
            {
                newChild.ConnectionWaypoints[waypoints.Key] = [..waypoints.Value];
            }
            
            return newChild;
        }

        internal void UpdateSymbolPackage(EditorSymbolPackage parentSymbolPackage)
        {
            _parentSymbolPackage = parentSymbolPackage;
        }

        internal Child Clone(SymbolUi parent, Symbol.Child symbolChild)
        {
            var clone = new Child(symbolChild.Id, parent._id, (EditorSymbolPackage)parent.Symbol.SymbolPackage)
                            {
                                PosOnCanvas = PosOnCanvas,
                                Size = Size,
                                Style = Style,
                                Comment = Comment,
                                SectionId = SectionId,
                                SnapshotGroupIndex = SnapshotGroupIndex,
                                SnapshotEnabledInputIds = SnapshotEnabledInputIds == null ? null : [..SnapshotEnabledInputIds],
                            };

            foreach (var overridePair in ConnectionStyleOverrides)
            {
                clone.ConnectionStyleOverrides[overridePair.Key] = overridePair.Value;
            }

            foreach (var waypoints in ConnectionWaypoints)
            {
                clone.ConnectionWaypoints[waypoints.Key] = [..waypoints.Value];
            }

            return clone;
        }

        public override string ToString()
        {
            return $"{SymbolChild.Parent.Name}>[{SymbolChild.ReadableName}]";
        }
    }
}