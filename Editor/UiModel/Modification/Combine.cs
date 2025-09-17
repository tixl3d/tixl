using System.Text;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Annotations;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.UiModel.Modification;

internal static class Combine
{
    // todo - convert to proper c# style code generation
    public static void CombineAsNewType(SymbolUi parentCompositionSymbolUi,
                                        EditableSymbolProject project,
                                        List<SymbolUi.Child> selectedChildUis,
                                        List<Annotation> selectedAnnotations,
                                        string newSymbolName,
                                        string nameSpace, string description, bool shouldBeTimeClip)
    {
        var executedCommands = new List<ICommand>();

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
                                 select (child, output, con)).ToList().Distinct();
        var connectionsToNewOutputs = new List<Symbol.Connection>(outputConnections.Length);
        int outputNameCounter = 2;
        var outputNameHashSet = new HashSet<string>();
        foreach (var (child, output, origConnection) in outputsToGenerate)
        {
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
                var slotString = "Slot<" + typeName + ">";
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
            return;
        }

        newSymbolUi.Description = description;
        newSymbolUi.FlagAsModified();

        // Apply content to new symbol
        var copyCmd = new CopySymbolChildrenCommand(parentCompositionSymbolUi, selectedChildUis, selectedAnnotations, newSymbolUi, Vector2.Zero);
        copyCmd.Do();
        executedCommands.Add(copyCmd);

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

        copyCmd.OldToNewIdDict.ToList().ForEach(x => oldToNewIdMap.Add(x.Key, x.Value));

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
        executedCommands.Add(addCommand);

        var newSymbolChildId = addCommand.AddedChildId;

        foreach (var con in inputConnections.Reverse()) // reverse for multi input order preservation
        {
            var sourceId = con.SourceParentOrChildId;
            var sourceSlotId = con.SourceSlotId;
            var targetId = newSymbolChildId;
            var targetSlotId = connectionToNewSlotIdMap[con];

            var newConnection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
            parentCompositionSymbol.AddConnection(newConnection);
        }

        foreach (var con in outputConnections.Reverse()) // reverse for multi input order preservation
        {
            var sourceId = newSymbolChildId;
            var sourceSlotId = connectionToNewSlotIdMap[con];
            var targetId = con.TargetParentOrChildId;
            var targetSlotId = con.TargetSlotId;

            var newConnection = new Symbol.Connection(sourceId, sourceSlotId, targetId, targetSlotId);
            parentCompositionSymbol.AddConnection(newConnection);
        }

        var deleteCmd = new DeleteSymbolChildrenCommand(parentCompositionSymbolUi, selectedChildUis);
        deleteCmd.Do();
        executedCommands.Add(deleteCmd);

        // Delete original annotations
        foreach (var annotation in selectedAnnotations)
        {
            var deleteAnnotationCommand = new DeleteAnnotationCommand(parentCompositionSymbolUi, annotation);
            deleteAnnotationCommand.Do();
            executedCommands.Add(deleteAnnotationCommand);
        }

        UndoRedoStack.Add(new MacroCommand("Combine into symbol", executedCommands));

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
}