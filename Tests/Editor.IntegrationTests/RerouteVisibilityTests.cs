using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.Compilation;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Windows.SymbolLib;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Xunit;
using Command = T3.Core.DataTypes.Command;

namespace Editor.IntegrationTests
{
    /// <summary>Checks browser visibility against real symbol/UI replacement and definition validation.</summary>
    [Collection("EditorModel")]
    public sealed class RerouteVisibilityTests
    {
        /// <summary>Scalar, vector, and command anchors keep their derived presentation across replacement.</summary>
        [Theory]
        [InlineData(typeof(float))]
        [InlineData(typeof(Vector3))]
        [InlineData(typeof(Command))]
        public void ReplacementAndConsistencyRefreshVisibility(Type valueType)
        {
            using var package = new TestPackage();
            var type = typeof(Types.Routing.TestReroute<>).MakeGenericType(valueType);
            var original = package.Add(type);
            Assert.True(original.HiddenFromBrowser);
            Assert.True(SymbolAnalysis.TryGetRerouteDefinition(original.Symbol, out var definition));
            Assert.Equal(original.Symbol.InputDefinitions[0].Id, definition.InputId);
            Assert.Equal(original.Symbol.OutputDefinitions[0].Id, definition.OutputId);

            // A replacement initially read without consistency updates must carry visibility too.
            var replacement = new SymbolUi(original.Symbol, false);
            original.ReplaceWith(replacement);
            Assert.True(original.HiddenFromBrowser);

            var output = original.Symbol.OutputDefinitions[0];
            original.Symbol.OutputDefinitions.Clear();
            original.UpdateConsistencyWithSymbol();
            Assert.False(original.HiddenFromBrowser);
            original.Symbol.OutputDefinitions.Add(output);
            original.UpdateConsistencyWithSymbol();
            Assert.True(original.HiddenFromBrowser);

            // Reload can replace the definition under the same ID while retaining the UI object.
            package.ReplaceDefinition(original.Symbol.Id, typeof(OrdinaryFloat));
            original.ReplaceWith(new SymbolUi(original.Symbol, true));
            Assert.False(original.HiddenFromBrowser);
            package.ReplaceDefinition(original.Symbol.Id, type);
            original.ReplaceWith(new SymbolUi(original.Symbol, true));
            Assert.True(original.HiddenFromBrowser);
        }

        /// <summary>Search and tree refresh honor visibility for empty, exact, namespace, and typed queries.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("rer")]
        [InlineData("TestReroute")]
        [InlineData("Types.Routing")]
        public void SearchAndTreeExcludeAnchors(string query)
        {
            using var package = new TestPackage();
            var anchor = package.Add(typeof(Types.Routing.TestReroute<float>));
            var ordinary = package.Add(typeof(OrdinaryFloat));
            var filter = new SymbolFilter { SearchString = query };
            filter.UpdateIfNecessary(null, forceUpdate: true);
            Assert.DoesNotContain(anchor, filter.MatchingSymbolUis);
            filter.FilterInputType = typeof(float);
            filter.FilterOutputType = typeof(float);
            filter.UpdateIfNecessary(null, forceUpdate: true);
            Assert.DoesNotContain(anchor, filter.MatchingSymbolUis);

            filter.SearchString = nameof(OrdinaryFloat);
            filter.UpdateIfNecessary(null, forceUpdate: true);
            Assert.Contains(ordinary, filter.MatchingSymbolUis);
            var tree = new NamespaceTreeNode("root");
            for (var refresh = 0; refresh < 2; refresh++)
            {
                tree.PopulateCompleteTree();
                var symbols = CollectSymbols(tree).ToArray();
                Assert.DoesNotContain(anchor.Symbol, symbols);
                Assert.Contains(ordinary.Symbol, symbols);
            }
        }

        /// <summary>Rejects malformed interfaces, slot shapes, foreign packages, and nonempty definitions.</summary>
        [Fact]
        public void DetectorRejectsUnsupportedDefinitions()
        {
            using var package = new TestPackage();
            Assert.False(SymbolAnalysis.IsReroute(package.Add(typeof(OrdinaryFloat)).Symbol));
            Assert.False(SymbolAnalysis.IsReroute(package.Add(typeof(Types.Routing.MultiReroute)).Symbol));

            using var foreignPackage = new TestPackage(Guid.NewGuid());
            Assert.False(SymbolAnalysis.IsReroute(foreignPackage.Add(typeof(Types.Routing.TestReroute<float>)).Symbol));

            var anchor = package.Add(typeof(Types.Routing.TestReroute<float>));
            var output = anchor.Symbol.OutputDefinitions[0];
            anchor.Symbol.OutputDefinitions[0] = new Symbol.OutputDefinition { Id = output.Id, ValueType = typeof(int) };
            Assert.False(SymbolAnalysis.IsReroute(anchor.Symbol));
            anchor.Symbol.OutputDefinitions[0] = new Symbol.OutputDefinition
                                                    { Id = output.Id, ValueType = typeof(float), OutputDataType = typeof(object) };
            Assert.False(SymbolAnalysis.IsReroute(anchor.Symbol));
            anchor.Symbol.OutputDefinitions[0] = output;
            anchor.Symbol.InputDefinitions.Add(anchor.Symbol.InputDefinitions[0]);
            Assert.False(SymbolAnalysis.IsReroute(anchor.Symbol));
            anchor.Symbol.InputDefinitions.RemoveAt(1);
            anchor.AddChild(package.Add(typeof(OrdinaryFloat)).Symbol, Guid.NewGuid(), Vector2.Zero, Vector2.One);
            Assert.False(SymbolAnalysis.IsReroute(anchor.Symbol));
        }

        /// <summary>Cloning derives visibility from the destination definition, without an ID-map requirement.</summary>
        [Fact]
        public void CloneUsesDestinationDefinition()
        {
            using var package = new TestPackage();
            var anchor = package.Add(typeof(Types.Routing.TestReroute<float>));
            var destination = package.Add(typeof(OrdinaryFloat));
            Assert.False(anchor.CloneForNewSymbol(destination.Symbol).HiddenFromBrowser);
            Assert.True(anchor.CloneForNewSymbol(anchor.Symbol).HiddenFromBrowser);

            var child = destination.AddChild(anchor.Symbol, Guid.NewGuid(), new Vector2(100, 200), MagGraphItem.RerouteSize);
            var item = new MagGraphItem { IsReroute = true, Selectable = child, Size = child.Size };
            Assert.False(item.SupportsBlockLayout);
            item.PosOnCanvas += new Vector2(37, -12);
            Assert.Equal(new Vector2(137, 188), child.PosOnCanvas);
            Assert.Equal(MagGraphItem.RerouteSize, child.Size);
        }

        private static IEnumerable<Symbol> CollectSymbols(NamespaceTreeNode node)
            => node.Symbols.Concat(node.Children.SelectMany(CollectSymbols));

        /// <summary>Automatic placement excludes anchors while movement, wiring, copying, and undo remain available.</summary>
        [Fact]
        public void LayoutEligibilityPreservesManualEditing()
        {
            var imgui = ImGui.CreateContext();
            try
            {
                using var package = new TestPackage();
                var home = package.Add(typeof(EmptyComposition));
                var ordinary = package.Add(typeof(OrdinaryFloat));
                var anchor = package.Add(typeof(Types.Routing.TestReroute<float>));
                var source = home.AddChild(ordinary.Symbol, Guid.NewGuid(), new Vector2(-400, 100), new Vector2(110, 25));
                var target = home.AddChild(ordinary.Symbol, Guid.NewGuid(), new Vector2(400, 100), new Vector2(110, 25));
                var dot = home.AddChild(anchor.Symbol, Guid.NewGuid(), new Vector2(230, 100), MagGraphItem.RerouteSize);
                var connection = new Symbol.Connection(source.Id, ordinary.Symbol.OutputDefinitions[0].Id,
                                                       target.Id, ordinary.Symbol.InputDefinitions[0].Id);
                home.Symbol.AddConnection(connection, 0);
                Assert.True(OpenedProject.TryCreateWithExplicitHome(package, home.Symbol.Id, out var opened, out _));
                var history = new NavigationHistory(opened.Structure);
                var selection = new NodeSelection(history, opened.Structure);
                // Layout has no dependency on the GPU-backed image background.
                var view = new ProjectView(opened, history, selection, null!);
                var canvas = (MagGraphView)Activator.CreateInstance(typeof(MagGraphView), BindingFlags.Instance | BindingFlags.NonPublic,
                                                                    null, [view], null)!;
                view.GraphView = canvas;
                var context = new GraphUiContext(view, canvas);
                context.Layout.ComputeLayout(context, true);
                context.Selector.SetSelection(dot, context.Layout.Items[dot.Id].Instance);
                Assert.False(TreeLayouting.LayoutInputsOfSelection(context));
                var dotPosition = dot.PosOnCanvas;
                context.Selector.AddSelection(target, context.Layout.Items[target.Id].Instance);
                Assert.True(TreeLayouting.LayoutInputsOfSelection(context));
                Assert.Equal(dotPosition, dot.PosOnCanvas);
                Assert.NotEqual(new Vector2(-400, 100), source.PosOnCanvas);
                Assert.False(context.Layout.Items[source.Id].Area.Overlaps(context.Layout.Items[dot.Id].Area));
                Assert.Single(home.Symbol.Connections);

                // The same drag entry point must reject a mixed selection as a whole.
                var movement = context.ItemMovement;
                movement.SetDraggedItems([source]);
                movement.StartDragOperation(context);
                Assert.NotEmpty(movement.SpliceSets);
                context.CancelMacroCommand();
                movement.SetDraggedItems([source, dot]);
                movement.StartDragOperation(context);
                Assert.Empty(movement.SpliceSets);
                context.CancelMacroCommand();
                movement.SetDraggedItems([dot]);
                movement.StartDragOperation(context);
                Assert.Empty(movement.SpliceSets);

                // Manual movement remains an ordinary undoable position edit with compact size.
                context.Layout.Items[dot.Id].PosOnCanvas += new Vector2(37, -12);
                context.MoveElementsCommand!.StoreCurrentValues();
                context.MoveElementsCommand.Undo();
                Assert.Equal(dotPosition, dot.PosOnCanvas);
                context.MoveElementsCommand.Do();
                Assert.Equal(dotPosition + new Vector2(37, -12), dot.PosOnCanvas);
                Assert.Equal(MagGraphItem.RerouteSize, dot.Size);
                context.CancelMacroCommand();

                var wire = new Symbol.Connection(source.Id, ordinary.Symbol.OutputDefinitions[0].Id,
                                                 dot.Id, anchor.Symbol.InputDefinitions[0].Id);
                var manualConnection = new AddConnectionCommand(home.Symbol, wire, 0);
                manualConnection.Do();
                Assert.Contains(wire, home.Symbol.Connections);
                manualConnection.Undo();
                Assert.DoesNotContain(wire, home.Symbol.Connections);
                manualConnection.Do();

                // Copy/paste uses the same graph identity and keeps internal wires and compact sizes.
                var copy = new CopySymbolChildrenCommand(home, [source, dot], null, home, new Vector2(800, 400));
                copy.Do();
                var copiedDotId = copy.OldToNewChildIds[dot.Id];
                Assert.Equal(MagGraphItem.RerouteSize, home.ChildUis[copiedDotId].Size);
                Assert.Contains(home.Symbol.Connections, c => c.TargetParentOrChildId == copiedDotId);
                copy.Undo();
                Assert.False(home.ChildUis.ContainsKey(copiedDotId));
                copy.Do();
                Assert.Equal(MagGraphItem.RerouteSize, home.ChildUis[copiedDotId].Size);

                manualConnection.Undo();
                home.Symbol.RemoveConnection(connection, 0);
                context.Layout.ComputeLayout(context, true);
                var sourceItem = context.Layout.Items[source.Id];
                var targetItem = context.Layout.Items[target.Id];
                var dotItem = context.Layout.Items[dot.Id];
                source.PosOnCanvas = Vector2.Zero;
                target.PosOnCanvas = new Vector2(MagGraphItem.Width, 0);
                dot.PosOnCanvas = target.PosOnCanvas;
                Assert.True(CountAutomaticConnections(sourceItem, targetItem) > 0);
                Assert.Equal(0, CountAutomaticConnections(sourceItem, dotItem));
                dot.PosOnCanvas = Vector2.Zero;
                Assert.Equal(0, CountAutomaticConnections(dotItem, targetItem));
                Assert.Equal(1, dotItem.GetInputAnchorCount());
                Assert.Equal(1, dotItem.GetOutputAnchorCount());

                // Run the actual drag snap loop in an ImGui frame with a positive ordinary-pair control.
                var io = ImGui.GetIO();
                io.DisplaySize = new Vector2(1280, 720);
                io.Fonts.AddFontDefault();
                io.Fonts.Build();
                ImGui.NewFrame();
                ImGui.Begin("Layout checks");
                dot.PosOnCanvas = new Vector2(-2000, -2000);
                Assert.True(DragSnaps(context, source));
                target.PosOnCanvas = new Vector2(-2000, -2000);
                dot.PosOnCanvas = new Vector2(MagGraphItem.Width, 0);
                Assert.False(DragSnaps(context, source));
                source.PosOnCanvas = new Vector2(-2000, -2000);
                dot.PosOnCanvas = Vector2.Zero;
                target.PosOnCanvas = new Vector2(MagGraphItem.Width, 0);
                Assert.False(DragSnaps(context, dot));
                ImGui.End();
                ImGui.EndFrame();
            }
            finally
            {
                T3.Editor.UiModel.Commands.UndoRedoStack.Clear();
                ImGui.DestroyContext(imgui);
            }
        }

        private static int CountAutomaticConnections(MagGraphItem source, MagGraphItem target)
        {
            var collect = typeof(MagItemMovement).GetMethod("GetPotentialConnectionsAfterSnap", BindingFlags.NonPublic | BindingFlags.Static)!;
            var candidates = (System.Collections.IList)Activator.CreateInstance(collect.GetParameters()[0].ParameterType.GetElementType()!)!;
            collect.Invoke(null, [candidates, source, target]);
            return candidates.Count;
        }

        private static bool DragSnaps(GraphUiContext context, SymbolUi.Child child)
        {
            context.ItemMovement.SetDraggedItems([child]);
            context.ItemMovement.StartDragOperation(context);
            typeof(MagItemMovement).GetMethod("HandleSnappedDragging", BindingFlags.NonPublic | BindingFlags.Instance)!
                                   .Invoke(context.ItemMovement, [context]);
            var snapping = typeof(MagItemMovement).GetField("_snapping", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var result = (bool)snapping.GetType().GetProperty("IsSnapped")!.GetValue(snapping)!;
            context.CancelMacroCommand();
            return result;
        }

        internal sealed class TestPackage : EditorSymbolPackage, IDisposable
        {
            // A null ID uses the supported TypeOperators package; a foreign ID exercises rejection.
            internal TestPackage(Guid? id = null) : base(AssemblyInformation.CreateUninitialized(), "", false)
            {
                _releaseInfo = new ReleaseInfo("tests.dll", Guid.Empty, id ?? SymbolAnalysis.TypeOperatorsPackageId,
                                              "Tests", new Version(4, 3), new Version(1, 0), false, []);
            }

            protected override ReleaseInfo ReleaseInfo => _releaseInfo;

            public override bool IsReadOnly => ReadOnly;

            internal bool ReadOnly;

            internal SymbolUi Add(Type type)
            {
                var symbol = ReplaceDefinition(Guid.NewGuid(), type);
                var ui = new SymbolUi(symbol, true) { ReadOnly = IsReadOnly };
                SymbolUiDict[symbol.Id] = ui;
                return ui;
            }

            internal Symbol ReplaceDefinition(Guid id, Type type)
            {
                // Use the loader's slot extraction without loading another copy of the test assembly.
                var definitions = new ConcurrentDictionary<Guid, OperatorTypeInfo>();
                typeof(AssemblyInformation).GetMethod("SetUpOperatorType", BindingFlags.NonPublic | BindingFlags.Static)!
                                           .Invoke(null, [type, definitions]);
                ((IDictionary<Guid, OperatorTypeInfo>)AssemblyInformation.OperatorTypeInfo)[id] = definitions.Single().Value;
                var symbol = CreateSymbol(type, id);
                SymbolDict[id] = symbol;
                return symbol;
            }

            private readonly ReleaseInfo _releaseInfo;
        }

        /// <summary>Ordinary scalar operator with slot IDs matching the test anchor for cloning.</summary>
        [Guid("a12b3f4f-9a99-4cbf-aab5-d9fb35a42461")]
        public sealed class OrdinaryFloat : Instance<OrdinaryFloat>
        {
            /// <summary>Scalar input shared with the anchor for clone compatibility.</summary>
            [Input(Guid = "c92c143f-23bd-412e-b063-1f885317285a")] public readonly InputSlot<float> Input = new();
            /// <summary>Scalar output shared with the anchor for clone compatibility.</summary>
            [Output(Guid = "a62dd467-1f29-436e-a8a8-ac2c085ee3f2")] public readonly Slot<float> Output = new();
        }

        /// <summary>Graph container for layout checks.</summary>
        [Guid("ee04d5da-537b-465d-a20c-a5b165c75dfb")]
        public sealed class EmptyComposition : Instance<EmptyComposition>;
    }
}

namespace Types.Routing
{
    /// <summary>Assembly-local marker matching the contract used by hot-reloaded operator packages.</summary>
    public interface IRerouteNode;

    /// <summary>Marked scalar anchor used with representative value types.</summary>
    [Guid("a445e188-72ca-41da-8c2b-33b78d3252f1")]
    public sealed class TestReroute<T> : Instance<TestReroute<T>>, IRerouteNode
    {
        /// <summary>Plain input required by the anchor shape contract.</summary>
        [Input(Guid = "c92c143f-23bd-412e-b063-1f885317285a")] public readonly InputSlot<T> Input = new();
        /// <summary>Matching plain output required by the anchor shape contract.</summary>
        [Output(Guid = "a62dd467-1f29-436e-a8a8-ac2c085ee3f2")] public readonly Slot<T> Output = new();
    }

    /// <summary>A marked bundle input must not be mistaken for a supported scalar anchor.</summary>
    [Guid("82721c1b-102f-4eb8-bd82-1839099a4138")]
    public sealed class MultiReroute : Instance<MultiReroute>, IRerouteNode
    {
        /// <summary>Unsupported multi-input shape.</summary>
        [Input(Guid = "c92c143f-23bd-412e-b063-1f885317285a")] public readonly MultiInputSlot<float> Input = new();
        /// <summary>Matching scalar output isolates rejection to the multi-input shape.</summary>
        [Output(Guid = "a62dd467-1f29-436e-a8a8-ac2c085ee3f2")] public readonly Slot<float> Output = new();
    }
}
