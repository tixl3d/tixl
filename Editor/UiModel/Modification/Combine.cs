using System.Text;
using T3.Core.DataTypes;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.SystemUi;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Sections;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.UiModel.Modification;

internal static class Combine
{
    // todo - convert to proper c# style code generation
    public static void CombineAsNewType(SymbolUi parentCompositionSymbolUi,
                                        EditableSymbolProject project,
                                        List<SymbolUi.Child> selectedChildUis,
                                        List<Section> selectedSections,
                                        string newSymbolName,
                                        string nameSpace, string description, bool shouldBeTimeClip)
    {
        Dictionary<Guid, Guid> oldToNewIdMap = new Dictionary<Guid, Guid>();
        Dictionary<Symbol.Connection, Guid> connectionToNewSlotIdMap = new Dictionary<Symbol.Connection, Guid>();

        // get all the connections that go into the selection (selected ops as target)
        var parentCompositionSymbol = parentCompositionSymbolUi.Symbol;
        var potentialTargetIds = from child in selectedChildUis select child.Id;
        var inputConnections = (from con in parentCompositionSymbol.Connections
                                from id in potentialTargetIds
                                where con.TargetParentOrChildId == id
                                where potentialTargetIds.All(potId => potId != con.SourceParentOrChildId)
                                select con).ToArray();
        var inputsToGenerate = (from con in inputConnections
                                from child in parentCompositionSymbol.Children.Values
                                where child.Id == con.TargetParentOrChildId
                                from input in child.Symbol.InputDefinitions
                                where input.Id == con.TargetSlotId
                                select (child, input, con)).ToList().Distinct().ToArray();
        var usingStringBuilder = new StringBuilder();
        var inputStringBuilder = new StringBuilder();
        var outputStringBuilder = new StringBuilder();
        var connectionsFromNewInputs = new List<Symbol.Connection>(inputConnections.Length);
        int inputNameCounter = 2;
        var inputNameHashSet = new HashSet<string>();
        foreach (var (child, input, origConnection) in inputsToGenerate)
        {
            var inputValueType = input.DefaultValue.ValueType;
            if (TypeNameRegistry.Entries.TryGetValue(inputValueType, out var typeName))
            {
                var @namespace = input.DefaultValue.ValueType.Namespace;
                usingStringBuilder.AppendLine("using " + @namespace + ";");
                Guid newInputGuid = Guid.NewGuid();
                connectionToNewSlotIdMap.Add(origConnection, newInputGuid);
                var attributeString = "        [Input(Guid = \"" + newInputGuid + "\")]";
                inputStringBuilder.AppendLine(attributeString);
                var newInputName = inputNameHashSet.Contains(input.Name) ? (input.Name + inputNameCounter++) : input.Name;
                inputNameHashSet.Add(newInputName);
                var slotString = (input.IsMultiInput ? "MultiInputSlot<" : "InputSlot<") + typeName + ">";
                var inputString = "        public readonly " + slotString + " " + newInputName + " = new " + slotString + "();";
                inputStringBuilder.AppendLine(inputString);
                inputStringBuilder.AppendLine("");

                var newConnection = new Symbol.Connection(Guid.Empty, newInputGuid, child.Id, input.Id);
                connectionsFromNewInputs.Add(newConnection);
            }
            else
            {
                Log.Error($"Error, no registered name found for typename: {input.DefaultValue.ValueType.Name}");
            }
        }

        var outputConnections = (from con in parentCompositionSymbol.Connections
                                 from id in potentialTargetIds
                                 where con.SourceParentOrChildId == id
                                 where potentialTargetIds.All(potId => potId != con.TargetParentOrChildId)
                                 select con).ToArray();
        var outputsToGenerate = (from con in outputConnections
                                 from child in parentCompositionSymbol.Children.Values
                                 where child.Id == con.SourceParentOrChildId
                                 from output in child.Symbol.OutputDefinitions
                                 where output.Id == con.SourceSlotId
                                 select (child, output, con)).ToList().Distinct().ToArray();

        // As a time clip, one output becomes the TimeClipSlot that places the symbol on the timeline —
        // prefer the first Command output (the usual render flow), else the first output of any type.
        var timeClipOutputIndex = -1;
        if (shouldBeTimeClip && outputsToGenerate.Length > 0)
        {
            timeClipOutputIndex = 0;
            for (var i = 0; i < outputsToGenerate.Length; i++)
            {
                if (outputsToGenerate[i].output.ValueType != typeof(Command))
                    continue;

                timeClipOutputIndex = i;
                break;
            }
        }

        var connectionsToNewOutputs = new List<Symbol.Connection>(outputConnections.Length);
        int outputNameCounter = 2;
        var outputNameHashSet = new HashSet<string>();
        for (var outputIndex = 0; outputIndex < outputsToGenerate.Length; outputIndex++)
        {
            var (child, output, origConnection) = outputsToGenerate[outputIndex];
            var outputValueType = output.ValueType;
            if (TypeNameRegistry.Entries.TryGetValue(outputValueType, out var typeName))
            {
                var @namespace = outputValueType.Namespace;
                usingStringBuilder.AppendLine("using " + @namespace + ";");
                Guid newOutputGuid = Guid.NewGuid();
                var attributeString = "        [Output(Guid = \"" + newOutputGuid + "\")]";
                outputStringBuilder.AppendLine(attributeString);
                var newOutputName = outputNameHashSet.Contains(output.Name) ? (output.Name + outputNameCounter++) : output.Name;
                outputNameHashSet.Add(newOutputName);
                var slotString = (outputIndex == timeClipOutputIndex ? "TimeClipSlot<" : "Slot<") + typeName + ">";
                var outputString = "        public readonly " + slotString + " " + newOutputName + " = new " + slotString + "();";
                outputStringBuilder.AppendLine(outputString);
                outputStringBuilder.AppendLine("");

                var newConnection = new Symbol.Connection(child.Id, output.Id, Guid.Empty, newOutputGuid);
                connectionsToNewOutputs.Add(newConnection);
                connectionToNewSlotIdMap.Add(origConnection, newOutputGuid);
            }
            else
            {
                Log.Error($"Error, no registered name found for typename: {output.ValueType.Name}");
            }
        }

        // A time clip without any outgoing connection would produce a symbol that can't appear on the
        // timeline — give it a default Command clip slot the user can wire up later.
        if (shouldBeTimeClip && timeClipOutputIndex == -1)
        {
            usingStringBuilder.AppendLine("using T3.Core.DataTypes;");
            outputStringBuilder.AppendLine("        [Output(Guid = \"" + Guid.NewGuid() + "\")]");
            outputStringBuilder.AppendLine("        public readonly TimeClipSlot<Command> Output = new TimeClipSlot<Command>();");
            outputStringBuilder.AppendLine("");
        }

        usingStringBuilder.AppendLine("using T3.Core.Operator;");
        usingStringBuilder.AppendLine("using T3.Core.Operator.Attributes;");
        usingStringBuilder.AppendLine("using T3.Core.Operator.Slots;");
        usingStringBuilder.AppendLine("using System.Runtime.InteropServices;");

        Guid newSymbolId = Guid.NewGuid();

        var classStringBuilder = new StringBuilder(usingStringBuilder.ToString());
        classStringBuilder.AppendLine("");
        classStringBuilder.Append("namespace ").Append(nameSpace);
        classStringBuilder.AppendLine("{");
        classStringBuilder.AppendLine("    [Guid(\"" + newSymbolId + "\")]");
        classStringBuilder.AppendFormat("    internal sealed class {0} : Instance<{0}>\n", newSymbolName);
        classStringBuilder.AppendLine("    {");
        classStringBuilder.Append(outputStringBuilder);
        classStringBuilder.AppendLine("");
        classStringBuilder.Append(inputStringBuilder);
        classStringBuilder.AppendLine("    }");
        classStringBuilder.AppendLine("}");
        classStringBuilder.AppendLine("");
        var newSource = classStringBuilder.ToString();
        Log.Debug(newSource);

        // compile new instance type

        var success = project.TryCompile(newSource, newSymbolName, newSymbolId, nameSpace, out var newSymbol, out var newSymbolUi, out var failureLog);
        if (!success)
        {
            Log.Error($"Could not compile new symbol '{newSymbolName}': {failureLog}");
            
            const string exit = "Exit";
            var choice = BlockingWindow.Instance.ShowMessageBox("""
                                                                Sadly the compilation of the combined operator filed.
                                                                
                                                                Potential reasons:
                                                                - An input name is using a known core type.
                                                                - Some other protected names or keywords are used 

                                                                """ + failureLog, 
                                                                "Can't compile", 
                                                                exit,
                                                                "Try to continue");
            
            if (choice != exit)
            {
                //reason = $"Failed to find soundTrack for [{symbol.Name}] - export cancelled, see log for details";
                return;
            }
            EditorUi.Instance.ExitApplication();
            return;
        }

        newSymbolUi.Description = description;
        newSymbolUi.FlagAsModified();

        // Apply content to new symbol
        var copyCmd = new CopySymbolChildrenCommand(parentCompositionSymbolUi, selectedChildUis, selectedSections, newSymbolUi, Vector2.Zero);
        copyCmd.Do();

        var newChildrenArea = GetAreaFromChildren(newSymbolUi.ChildUis.Values);

        // Initialize output positions
        if (newSymbolUi.OutputUis.Count > 0)
        {
            var firstOutputPosition = new Vector2(newChildrenArea.Max.X + 300, (newChildrenArea.Min.Y + newChildrenArea.Max.Y) / 2);

            foreach (var outputUi in newSymbolUi.OutputUis.Values)
            {
                outputUi.PosOnCanvas = firstOutputPosition;
                firstOutputPosition += new Vector2(0, 100);
            }
        }

        copyCmd.OldToNewChildIds.ToList().ForEach(x => oldToNewIdMap.Add(x.Key, x.Value));

        // A combined time clip is born with its authored source extent set to the union of the copied
        // content (keyframes and nested clips), so its first placement on a timeline is correctly sized.
        if (shouldBeTimeClip)
            InitSourceExtentFromContent(newSymbolUi);

        MoveSnapshotsToNewSymbol(parentCompositionSymbol.Id, newSymbol.Id, oldToNewIdMap);

        var selectedChildrenIds = (from child in selectedChildUis select child.Id).ToList();
        parentCompositionSymbol.Animator.RemoveAnimationsFromInstances(selectedChildrenIds);

        foreach (var con in connectionsFromNewInputs)
        {
            var sourceId = con.SourceParentOrChildId;
            var sourceSlotId = con.SourceSlotId;
            var targetId = oldToNewIdMap[con.TargetParentOrChildId];
            var targetSlotId = con.TargetSlotId;

            var newConnection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
            newSymbol.AddConnection(newConnection);
        }

        foreach (var con in connectionsToNewOutputs)
        {
            var sourceId = oldToNewIdMap[con.SourceParentOrChildId];
            var sourceSlotId = con.SourceSlotId;
            var targetId = con.TargetParentOrChildId;
            var targetSlotId = con.TargetSlotId;

            var newConnection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
            newSymbol.AddConnection(newConnection);
        }

        // Insert instance of new symbol
        var originalChildrenArea = GetAreaFromChildren(selectedChildUis);
        var addCommand = new AddSymbolChildCommand(parentCompositionSymbolUi.Symbol, newSymbol.Id)
                             { PosOnCanvas = originalChildrenArea.GetCenter() };

        addCommand.Do();

        var newSymbolChildId = addCommand.AddedChildId;

        for (var i = inputConnections.Length - 1; i >= 0; i--) // reverse for multi input order preservation
        {
            var con = inputConnections[i];
            var sourceId = con.SourceParentOrChildId;
            var sourceSlotId = con.SourceSlotId;
            var targetId = newSymbolChildId;
            var targetSlotId = connectionToNewSlotIdMap[con];

            var newConnection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
            parentCompositionSymbol.AddConnection(newConnection);
        }

        for (var i = outputConnections.Length - 1; i >= 0; i--) // reverse for multi input order preservation
        {
            var con = outputConnections[i];
            var sourceId = newSymbolChildId;
            var sourceSlotId = connectionToNewSlotIdMap[con];
            var targetId = con.TargetParentOrChildId;
            var targetSlotId = con.TargetSlotId;

            var newConnection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
            parentCompositionSymbol.AddConnection(newConnection);
        }

        var deleteCmd = new DeleteSymbolChildrenCommand(parentCompositionSymbolUi, selectedChildUis);
        deleteCmd.Do();

        // Delete original sections
        foreach (var section in selectedSections)
        {
            var deleteSectionCommand = new DeleteSectionCommand(parentCompositionSymbolUi, section);
            deleteSectionCommand.Do();
        }

        RemoveUnusedSnapshotsFromParent(parentCompositionSymbol);

        // Creating a new symbol/assembly can't be cleanly undone (undoing the children delete would
        // orphan the new operator), so drop the history rather than leave it inconsistent.
        UndoRedoStack.Clear();

        // Sadly saving in background does not save the source files.
        // This needs to be fixed.
        //T3Ui.SaveInBackground(false);
        project.SaveModifiedSymbols();
    }

    private static ImRect GetAreaFromChildren(IEnumerable<SymbolUi.Child> childUis)
    {
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        var hasAtLeastOne = false;
        foreach (var childUi in childUis)
        {
            min = Vector2.Min(min, childUi.PosOnCanvas);
            max = Vector2.Max(max, childUi.PosOnCanvas + childUi.Size);
            hasAtLeastOne = true;
        }

        if (!hasAtLeastOne)
        {
            return new ImRect(new Vector2(-100, -100),
                                       new Vector2(100, 100));
        }

        return new ImRect(min, max);
    }

    /// <summary>
    /// Moves the parent composition's snapshot data for the combined children into the new symbol's
    /// variation pool. Parameter sets are re-keyed from the old child ids to the copied children's new
    /// ids; variation ids, titles and activation indices are kept so the moved snapshots stay
    /// recognizable next to their originals. Pruning the parent's now-dangling entries is left to
    /// <see cref="RemoveUnusedSnapshotsFromParent"/>, which runs after the originals are deleted.
    /// </summary>
    private static void MoveSnapshotsToNewSymbol(Guid parentSymbolId, Guid newSymbolId, Dictionary<Guid, Guid> oldToNewIdMap)
    {
        // Only user variations can move — a combinable composition lives in an editable project,
        // so read-only package defaults don't apply here.
        var parentPool = VariationHandling.GetOrLoadVariations(parentSymbolId);
        if (parentPool.UserVariations.Count == 0)
            return;

        var newPool = VariationHandling.GetOrLoadVariations(newSymbolId);
        var movedAny = false;

        foreach (var variation in parentPool.UserVariations)
        {
            // Collect the moved parameter sets from a clone so the values don't stay shared between pools.
            var movedVariation = variation.Clone();
            var movedSets = new Dictionary<Guid, Dictionary<Guid, InputValue>>();
            foreach (var (childId, parameterSet) in movedVariation.ParameterSetsForChildIds)
            {
                if (oldToNewIdMap.TryGetValue(childId, out var newChildId))
                {
                    movedSets[newChildId] = parameterSet;
                }
            }

            if (movedSets.Count == 0)
                continue;

            movedVariation.ParameterSetsForChildIds = movedSets;
            newPool.AddUserVariation(movedVariation);
            movedAny = true;
        }

        if (movedAny)
            newPool.SaveVariationsToFile();
    }

    /// <summary>
    /// Strips snapshot entries that reference children no longer present in the parent composition and
    /// removes snapshots that end up with no entries at all. Catches both the children just replaced by
    /// the combine and entries that were already stale from earlier deletions. Presets (keyed by
    /// Guid.Empty for the composition's own inputs) are untouched. Must run after the combined
    /// originals were deleted from the parent.
    /// </summary>
    private static void RemoveUnusedSnapshotsFromParent(Symbol parentSymbol)
    {
        var parentPool = VariationHandling.GetOrLoadVariations(parentSymbol.Id);
        List<Variation> unusedVariations = null;
        var poolModified = false;

        foreach (var variation in parentPool.UserVariations)
        {
            List<Guid> danglingChildIds = null;
            foreach (var childId in variation.ParameterSetsForChildIds.Keys)
            {
                if (childId == Guid.Empty || parentSymbol.Children.ContainsKey(childId))
                    continue;

                danglingChildIds ??= [];
                danglingChildIds.Add(childId);
            }

            if (danglingChildIds == null)
                continue;

            foreach (var childId in danglingChildIds)
            {
                variation.ParameterSetsForChildIds.Remove(childId);
            }

            poolModified = true;

            if (variation.ParameterSetsForChildIds.Count == 0)
            {
                unusedVariations ??= [];
                unusedVariations.Add(variation);
            }
        }

        if (!poolModified)
            return;

        if (unusedVariations != null)
        {
            foreach (var variation in unusedVariations)
            {
                parentPool.RemoveUserVariation(variation);
            }
        }

        parentPool.SaveVariationsToFile();
    }

    /// <summary>
    /// Sets the new symbol's authored source extent (<see cref="Gui.Windows.TimeLine.TimelineState.SourceExtent"/>)
    /// to the union of the copied content: all keyframes and all nested time clips. Skipped when the
    /// content has no time span (e.g. static ops only).
    /// </summary>
    private static void InitSourceExtentFromContent(SymbolUi newSymbolUi)
    {
        var range = T3.Core.Animation.TimeRange.Undefined;
        var symbol = newSymbolUi.Symbol;

        foreach (var child in symbol.Children.Values)
        {
            foreach (var inputDef in child.Symbol.InputDefinitions)
            {
                if (!symbol.Animator.TryGetCurvesForChildInput(child.Id, inputDef.Id, out var curves))
                    continue;

                foreach (var curve in curves)
                {
                    foreach (var vDefinition in curve.GetVDefinitions())
                    {
                        range.Unite((float)vDefinition.U);
                    }
                }
            }

            foreach (var output in child.Outputs.Values)
            {
                if (output.OutputData is T3.Core.Animation.TimeClip timeClip)
                    range.Unite(timeClip.TimeRange);
            }
        }

        if (!range.IsValid || range.Duration <= 0)
            return;

        newSymbolUi.TimelineState ??= new Gui.Windows.TimeLine.TimelineState();
        newSymbolUi.TimelineState.SourceExtent = range;
    }
}