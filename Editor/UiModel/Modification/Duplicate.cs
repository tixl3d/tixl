using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Compilation;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using GraphUtils = T3.Editor.UiModel.Helpers.GraphUtils;

namespace T3.Editor.UiModel.Modification;

internal static class Duplicate
{
    public static Symbol DuplicateAsNewType(SymbolUi compositionUi, EditableSymbolProject project, Guid symbolId, string newTypeName, string nameSpace,
                                            string description, Vector2 posOnCanvas)
    {
        var sourceSymbol = EditorSymbolPackage.AllSymbols.FirstOrDefault(x => x.Id == symbolId);
        if (sourceSymbol == null)
        {
            Log.Warning("Can't find symbol to duplicate");
            return null;
        }

        //var sourceSymbol = symbolChildToDuplicate.Symbol;

        var syntaxTree = GraphUtils.GetSyntaxTree(sourceSymbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{sourceSymbol.Name}' source.");
            return null;
        }

        // create new source on basis of original type
        var root = syntaxTree.GetRoot();
        var classRenamer = new ClassRenameRewriter(newTypeName);
        root = classRenamer.Visit(root);

        var memberRewriter = new Duplicate.MemberDuplicateRewriter(sourceSymbol.Name, newTypeName);

        root = memberRewriter.Visit(root);
        var oldToNewIdMap = memberRewriter.OldToNewGuidDict;

        // Symbol.Namespace comes from reflection and lacks the '@' escapes present in source code
        // (e.g. Lib.numbers.@float.basic), so a plain text replace can miss the declaration and leave
        // the duplicate in the source namespace — rewrite the declaration node instead.
        if (!GraphUtils.TryConvertToValidCodeNamespace(nameSpace, out var codeNamespace))
        {
            Log.Error($"'{nameSpace}' is not a valid namespace.");
            return null;
        }

        var namespaceRewriter = new NamespaceRenameRewriter(codeNamespace);
        root = namespaceRewriter.Visit(root);

        var newSource = root.GetText().ToString();

        var newSymbolId = Guid.NewGuid();
        newSource = ReplaceGuidAttributeWith(newSymbolId, newSource);
        Log.Debug(newSource);

        var sourceSymbolUi = sourceSymbol.GetSymbolUi();
        
        if (!project.TryCompile(newSource, newTypeName, newSymbolId, nameSpace, out var newSymbol, out _, out var failureLog))
        {
            Log.Error($"Could not compile new symbol '{newTypeName}': {failureLog}");
            return null;
        }
        
        var newSymbolUi = sourceSymbolUi.CloneForNewSymbol(newSymbol, oldToNewIdMap);
        newSymbolUi.Description = description;
        newSymbolUi.ReadOnly = false;

        // Carry the project settings (soundtrack, BPM, audio mix, export, proxy) over to the
        // duplicate. Clips get fresh ids so the two symbols never share audio-stream keys.
        newSymbol.CompositionSettings = sourceSymbol.CompositionSettings.Clone();
        foreach (var audioClip in newSymbol.CompositionSettings.Playback.AudioClips)
        {
            audioClip.Id = Guid.NewGuid();
        }

        project.ReplaceSymbolUi(newSymbolUi);

        // Apply content to a new symbol
        var cmd = new CopySymbolChildrenCommand(sourceSymbolUi,
                                                null,
                                                null,
                                                newSymbolUi,
                                                Vector2.One);
        cmd.Do();
        cmd.OldToNewChildIds.ToList().ForEach(x => oldToNewIdMap.Add(x.Key, x.Value));

        // Now copy connection from/to inputs/outputs that are not copied with the command 
        // todo: same code as in Symbol.SetInstanceType, factor out common code
        var connectionsToCopy = sourceSymbol.Connections.FindAll(c => c.IsConnectedToSymbolInput || c.IsConnectedToSymbolOutput);
        var connectionEntriesToReplace = new List<ConnectionEntry>(connectionsToCopy.Count);
        foreach (var con in connectionsToCopy)
        {
            var entry = new Duplicate.ConnectionEntry
                            {
                                Connection = con,
                                MultiInputIndex = sourceSymbol.Connections.FindAll(c => c.TargetParentOrChildId == con.TargetParentOrChildId
                                                                                        && c.TargetSlotId == con.TargetSlotId)
                                                               // ReSharper disable once PossibleUnintendedReferenceComparison
                                                              .FindIndex(cc => cc == con)
                            };
            connectionEntriesToReplace.Add(entry);
        }

        foreach (var conEntry in connectionEntriesToReplace)
        {
            var conToCopy = conEntry.Connection;
            var isInputConnection = conToCopy.IsConnectedToSymbolInput;
            var newSourceSlotId = isInputConnection ? oldToNewIdMap[conToCopy.SourceSlotId] : conToCopy.SourceSlotId;
            var newSourceId = isInputConnection ? conToCopy.SourceParentOrChildId : oldToNewIdMap[conToCopy.SourceParentOrChildId];

            var isOutputConnection = conToCopy.IsConnectedToSymbolOutput;
            var newTargetSlotId = isOutputConnection ? oldToNewIdMap[conToCopy.TargetSlotId] : conToCopy.TargetSlotId;
            var newTargetId = isOutputConnection ? conToCopy.TargetParentOrChildId : oldToNewIdMap[conToCopy.TargetParentOrChildId];

            var newConnection = new Symbol.Connection(newSourceId, newSourceSlotId, newTargetId, newTargetSlotId);
            newSymbol.AddConnection(newConnection, conEntry.MultiInputIndex);
        }

        // Copy the values of the input of the duplicated type: default values of symbol and the ones in composition context
        foreach (var sourceInputDef in sourceSymbol.InputDefinitions)
        {
            Guid newInputId = oldToNewIdMap[sourceInputDef.Id];
            var correspondingInputDef = newSymbol.InputDefinitions.Find(newInputDef => newInputDef.Id == newInputId);
            correspondingInputDef.DefaultValue = sourceInputDef.DefaultValue.Clone();
        }
        
        // Copy the values of the input of the duplicated type: default values of symbol and the ones in composition context
        foreach (var (sourceInputId, sourceInputUi) in sourceSymbolUi.InputUis)
        {
            var newInputId = oldToNewIdMap[sourceInputId];
            if (newSymbolUi.InputUis.TryGetValue(newInputId, out var newInputUi))
            {
                newInputUi.AddPadding = sourceInputUi.AddPadding;
                newInputUi.GroupTitle = sourceInputUi.GroupTitle;
                newInputUi.Description = sourceInputUi.Description;
                newInputUi.ExcludedFromPresets = sourceInputUi.ExcludedFromPresets;
                newInputUi.Relevancy = sourceInputUi.Relevancy;

                // This is a very unfortunate code, indeed. 
                // But implementing this as a generic turned out to be rather tricky
                // because you can't cast to a generic or infer the specific type without
                // adding a non-generic abstract base type.
                switch (sourceInputUi)
                {
                    case FloatInputUi srcFloatInput when 
                        newInputUi is FloatInputUi newFloatInput:
                        srcFloatInput.CopyTo(newFloatInput);
                        break;
                    case Vector2InputUi srcVec2Input when 
                        newInputUi is Vector2InputUi newVec2Input:
                        srcVec2Input.CopyTo(newVec2Input);
                        newVec2Input.UseVec2Control = srcVec2Input.UseVec2Control;
                        break;
                    case Vector3InputUi srcVec3Input when 
                        newInputUi is Vector3InputUi newVec3Input:
                        srcVec3Input.CopyTo(newVec3Input);
                        break;
                    case Vector4InputUi srcVec4Input when 
                        newInputUi is Vector4InputUi newVec4Input:
                        srcVec4Input.CopyTo(newVec4Input);
                        break;
                }
            }
        }

        // Create instance
        var addCommand = new AddSymbolChildCommand(compositionUi.Symbol, newSymbol.Id) { PosOnCanvas = posOnCanvas };
        UndoRedoStack.AddAndExecute(addCommand);

        // Update the positions
        var sourceSelectables = sourceSymbolUi.GetSelectables().ToArray();
        var newSelectables = newSymbolUi.GetSelectables().ToArray();
        Debug.Assert(sourceSelectables.Length == newSelectables.Length);
        for (int i = 0; i < sourceSelectables.Length && i < newSelectables.Length; i++)
        {
            newSelectables[i].PosOnCanvas = sourceSelectables[i].PosOnCanvas; // todo: check if this is enough or if id check needed
        }

        DuplicateVariations(sourceSymbol.Id, newSymbol.Id, oldToNewIdMap);

        Log.Debug($"Created new symbol '{newTypeName}'");

        newSymbolUi.FlagAsModified();
        compositionUi.FlagAsModified();
        project.SaveModifiedSymbols();

        // Creating a new symbol/assembly can't be cleanly undone (undoing the instance add would
        // orphan the new operator), so drop the history rather than leave it inconsistent.
        UndoRedoStack.Clear();

        return newSymbol;
    }

    /// <summary>
    /// Copies the source symbol's presets and snapshots into the new symbol's variation pool.
    /// Presets are keyed by the symbol's own input ids and snapshots by child ids — both were
    /// regenerated during duplication, so they are remapped through the id map. Entries that
    /// don't resolve (stale references in the source variation file) are skipped.
    /// </summary>
    private static void DuplicateVariations(Guid sourceSymbolId, Guid newSymbolId, Dictionary<Guid, Guid> oldToNewIdMap)
    {
        var sourcePool = VariationHandling.GetOrLoadVariations(sourceSymbolId);
        if (sourcePool.AllVariations.Count == 0)
            return;

        var newPool = VariationHandling.GetOrLoadVariations(newSymbolId);

        foreach (var variation in sourcePool.AllVariations)
        {
            var newVariation = variation.Clone();
            var remappedSets = new Dictionary<Guid, Dictionary<Guid, InputValue>>(newVariation.ParameterSetsForChildIds.Count);

            foreach (var (childId, parameterSet) in newVariation.ParameterSetsForChildIds)
            {
                if (childId == Guid.Empty)
                {
                    // Preset values reference the duplicated symbol's own inputs.
                    var remappedParameters = new Dictionary<Guid, InputValue>(parameterSet.Count);
                    foreach (var (inputId, value) in parameterSet)
                    {
                        if (oldToNewIdMap.TryGetValue(inputId, out var newInputId))
                        {
                            remappedParameters[newInputId] = value;
                        }
                    }

                    if (remappedParameters.Count > 0)
                    {
                        remappedSets[Guid.Empty] = remappedParameters;
                    }
                }
                else if (oldToNewIdMap.TryGetValue(childId, out var newChildId))
                {
                    // Snapshot values are keyed by the child's own symbol inputs, which are unchanged.
                    remappedSets[newChildId] = parameterSet;
                }
            }

            newVariation.ParameterSetsForChildIds = remappedSets;
            newPool.AddUserVariation(newVariation);
        }

        newPool.SaveVariationsToFile();
    }

    private static string ReplaceGuidAttributeWith(Guid newSymbolId, string newSource)
    {
        const string guidTagStart = "Guid(\"";
        int start = newSource.IndexOf(guidTagStart, StringComparison.Ordinal);
        if (start < 0)
            return newSource;

        start += guidTagStart.Length;
        int end = newSource.IndexOf("\")", start, StringComparison.Ordinal);
        if (end < 0)
            return newSource;

        var oldGuid = newSource[start..end];
        var newGuid = newSymbolId.ToString();
        return newSource.Replace(oldGuid, newGuid);
    }

    private sealed class ConnectionEntry
    {
        public Symbol.Connection Connection { get; set; }
        public int MultiInputIndex { get; set; }
    }

    private sealed class NamespaceRenameRewriter : CSharpSyntaxRewriter
    {
        private readonly string _newNamespace;

        public NamespaceRenameRewriter(string newNamespace)
        {
            _newNamespace = newNamespace;
        }

        public override SyntaxNode VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            return node.WithName(SyntaxFactory.ParseName(_newNamespace).WithTriviaFrom(node.Name));
        }

        public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            return node.WithName(SyntaxFactory.ParseName(_newNamespace).WithTriviaFrom(node.Name));
        }
    }

    private sealed class MemberDuplicateRewriter : CSharpSyntaxRewriter
    {
        private readonly string _oldSymbolName;
        private readonly string _newSymbolName;

        public MemberDuplicateRewriter(string oldSymbolName, string newSymbolName)
        {
            _oldSymbolName = oldSymbolName;
            _newSymbolName = newSymbolName;
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            // Only rewrite constructors of the original top-level class
            if (node.Identifier.Text != _oldSymbolName)
                return node;

            return node.WithIdentifier(
                                       SyntaxFactory.Identifier(_newSymbolName)
                                                    .WithTriviaFrom(node.Identifier));
        }
        
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Declaration.Type is not GenericNameSyntax nameSyntax)
                return node;

            var idValue = nameSyntax.Identifier.ValueText;

            // Only process specific slot types
            if (idValue is not ("InputSlot" or "MultiInputSlot" or "Slot" or "TimeClipSlot" or "TransformCallbackSlot"))
                return node;

            // Iterate through all attribute lists associated with the field
            foreach (var attrList in node.AttributeLists)
            {
                foreach (var attribute in attrList.Attributes)
                {
                    // Check if the attribute has a GUID in it
                    var match = _guidRegex.Match(attribute.ToString());
                    if (!match.Success)
                        continue; // Skip attributes without a GUID

                    var oldGuidString = match.Value;
                    if (!Guid.TryParse(oldGuidString, out var oldGuid))
                    {
                        Log.Debug("Skipping input with inconsistent GUID format: " + node);
                        continue;
                    }

                    // Generate a new GUID and update the dictionary
                    var newGuid = Guid.NewGuid();
                    OldToNewGuidDict[oldGuid] = newGuid;

                    // Replace the old GUID with the new one in the attribute's argument list
                    if (attribute.ArgumentList != null)
                    {
                        var updatedArgs = attribute.ArgumentList.ToString().Replace(oldGuidString, newGuid.ToString());
                        var newArgList = SyntaxFactory.ParseAttributeArgumentList(updatedArgs);

                        if (newArgList != null)
                        {
                            // Replace the old argument list with the new one
                            node = node.ReplaceNode(attribute.ArgumentList, newArgList);
                        }
                        else
                        {
                            Log.Debug("Skipping input with inconsistent argument list: " + node);
                        }
                    }
                    else
                    {
                        Log.Debug("Skipping input without argument list: " + node);
                    }
                }
            }

            return node;
        }

        private readonly Regex _guidRegex = new(@"(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}",
                                                RegexOptions.IgnoreCase);

        public Dictionary<Guid, Guid> OldToNewGuidDict { get; } = new(10);
    }
}