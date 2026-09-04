#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using T3.Core.Model;
using T3.Core.Operator;
using GraphUtils = T3.Editor.UiModel.Helpers.GraphUtils;

namespace T3.Editor.UiModel.Modification;

internal static class InputsAndOutputs
{
    public static bool RemoveInputsAndOutputsFromSymbol(Guid[] inputIdsToRemove, Guid[] outputIdsToRemove, Symbol symbol)
    {
        if (inputIdsToRemove.Length == 0 && outputIdsToRemove.Length == 0)
            return true;
        
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{symbol.Name}' source.");
            return false;
        }
        
        var root = syntaxTree.GetRoot();

        if (inputIdsToRemove.Length > 0)
        {
            if (!TryRemoveNodesByIdFromTree(inputIdsToRemove, root, out root))
                return false;
        }

        if (outputIdsToRemove.Length > 0)
        {
            if (!TryRemoveNodesByIdFromTree(outputIdsToRemove, root, out root))
                return false;
        }

        var newSource = root.GetText().ToString();
        Log.Debug(newSource);

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, true, out _);

        static bool TryRemoveNodesByIdFromTree(Guid[] inputIdsToRemove, SyntaxNode root, [NotNullWhen(true)] out SyntaxNode? rootWithoutRemoved)
        {
            var nodeFinder = new NodeByAttributeIdFinder(inputIdsToRemove);
            var newRoot = nodeFinder.Visit(root);

            rootWithoutRemoved = newRoot.RemoveNodes(nodeFinder.NodesToRemove, SyntaxRemoveOptions.KeepNoTrivia);
            return rootWithoutRemoved != null;
        }
    }

    private sealed class NodeByAttributeIdFinder(Guid[]? inputIds) : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.AttributeLists.Count > 0) {
                var attrList = node.AttributeLists[0];
                var searchedNodes = (from attribute in attrList.Attributes
                                    from id in _ids
                                    where attribute.ToString().ToLower().Contains(id.ToString().ToLower())
                                    select attribute).ToArray();

                if (searchedNodes.Length > 0)
                {
                    NodesToRemove.Add(node);
                }
            }
            return node;
        }

        private readonly Guid[] _ids = inputIds ?? Array.Empty<Guid>();
        public List<SyntaxNode> NodesToRemove { get; } = new();
    }

    private sealed class InputNodeByTypeFinder : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (!(node.Declaration.Type is GenericNameSyntax nameSyntax))
                return node;

            var idValue = nameSyntax.Identifier.ValueText;
            if (idValue == "InputSlot" || idValue == "MultiInputSlot")
                LastInputNodeFound = node;
            else if (LastInputNodeFound == null && idValue == "Slot")
                LastInputNodeFound = node;

            return node;
        }

        public SyntaxNode? LastInputNodeFound { get; private set; }
    }

    private sealed class OutputNodeByTypeFinder : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (!(node.Declaration.Type is GenericNameSyntax nameSyntax))
                return node;

            string idValue = nameSyntax.Identifier.ValueText;
            if (idValue == "Slot" || idValue == "TimeClipSlot") // find general way to support all output types here
                LastOutputNodeFound = node;

            return node;
        }

        public SyntaxNode? LastOutputNodeFound { get; private set; }
    }

    private sealed class ClassDeclarationFinder : CSharpSyntaxWalker
    {
        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ClassDeclarationNode = node;
        }

        public ClassDeclarationSyntax? ClassDeclarationNode;
    }

    private sealed class AllInputNodesFinder : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (!(node.Declaration.Type is GenericNameSyntax nameSyntax))
                return node;

            string idValue = nameSyntax.Identifier.ValueText;
            if (idValue == "InputSlot" || idValue == "MultiInputSlot")
            {
                var first = node.Declaration.Variables[0];
                var id = first.Identifier.ValueText;
                InputNodesFound.Add((id, node));
            }

            return node;
        }

        public List<(string, SyntaxNode)> InputNodesFound { get; } = new();
    }

    private sealed class AllInputNodesReplacer : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Declaration.Type is not GenericNameSyntax nameSyntax)
                return node;

            var idValue = nameSyntax.Identifier.ValueText;
            if (idValue == "InputSlot" || idValue == "MultiInputSlot")
            {
                return ReplacementNodes[_index++];
            }

            return node;
        }

        private int _index;
        public SyntaxNode[] ReplacementNodes =[];
    }

    public static bool AddInputToSymbol(Guid inputId, string inputName, bool multiInput, Type inputType, Symbol symbol)
    {
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{symbol.Name}' source.");
            return false;
        }

        var root = syntaxTree.GetRoot();

        var inputNodeFinder = new InputNodeByTypeFinder();
        var blockFinder = new ClassDeclarationFinder();
        
        if (inputNodeFinder.LastInputNodeFound == null)
        {
            blockFinder.Visit(root);
            if (blockFinder.ClassDeclarationNode == null)
            {
                Log.Error("Can't find class declaration.");
                return false;
            }
        }
        
        root = inputNodeFinder.Visit(root);
        
        var @namespace = inputType.Namespace;
        if (@namespace == "System")
            @namespace = String.Empty;
        else
            @namespace += ".";
        var attributeString = "\n        [Input(Guid = \"" + inputId + "\")]\n";
        var typeName = TypeNameRegistry.Entries[inputType];
        var slotString = (multiInput ? "MultiInputSlot<" : "InputSlot<") + @namespace + typeName + ">";
        var inputString = "        public readonly " + slotString + " " + inputName + " = new " + slotString + "();\n";

        var inputDeclaration = SyntaxFactory.ParseMemberDeclaration(attributeString + inputString);
        if (inputDeclaration == null)
        {
            Log.Error("Invalid input declaration");
            return false;
        }
        if (inputNodeFinder.LastInputNodeFound != null)
        {
            root = root.InsertNodesAfter(inputNodeFinder.LastInputNodeFound, 
                                         new[] { inputDeclaration });
        }
        else if (blockFinder.ClassDeclarationNode != null)
        {
            var node = blockFinder.ClassDeclarationNode;
            var classDeclaration = node.AddMembers(inputDeclaration);
            root = root.ReplaceNode(blockFinder.ClassDeclarationNode, classDeclaration);
        }
        else
        {
            var theClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            var classDeclarationSyntax = theClass.AddMembers(inputDeclaration);
            Log.Info($"{classDeclarationSyntax}");
            return false;
        }

        var newSource = root.GetText().ToString();
        Log.Debug(newSource);

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, false, out _);
    }

    // todo - factor out common code between AddInputToSymbol and AddOutputToSymbol
    public static bool AddOutputToSymbol(Guid outputId, string outputName, bool isTimeClipOutput, Type outputType, Symbol symbol)
    {
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{symbol.Name}' source.");
            return false;
        }

        var root = syntaxTree.GetRoot();

        var outputNodeFinder = new InputsAndOutputs.OutputNodeByTypeFinder();
        root = outputNodeFinder.Visit(root);
        
        var blockFinder = new InputsAndOutputs.ClassDeclarationFinder();
        if (outputNodeFinder.LastOutputNodeFound == null)
        {
            blockFinder.Visit(root);
            if (blockFinder.ClassDeclarationNode == null)
            {
                Log.Error("Could not add an output as no previous one was found, this case is missing and must be added.");
                return false;
            }
        }

        var @namespace = outputType.Namespace;
        if (@namespace == "System")
            @namespace = String.Empty;
        else
            @namespace += ".";
        var attributeString = "\n        [Output(Guid = \"" + outputId + "\")]\n";
        var typeName = TypeNameRegistry.Entries[outputType];
        var slotString = (isTimeClipOutput ? "TimeClipSlot<" : "Slot<") + @namespace + typeName + ">";
        var inputString = $"        public readonly {slotString} {outputName} = new {slotString}();\n";

        var outputDeclaration = SyntaxFactory.ParseMemberDeclaration(attributeString + inputString);
        if (outputDeclaration == null)
        {
            Log.Error("Invalid output declaration");
            return false;
        }
        if (outputNodeFinder.LastOutputNodeFound != null)
        {
            root = root.InsertNodesAfter(outputNodeFinder.LastOutputNodeFound, new[] { outputDeclaration });
        }
        else if (blockFinder.ClassDeclarationNode != null)
        {
            var node = blockFinder.ClassDeclarationNode;
            var classDeclaration = node.AddMembers(outputDeclaration);
            root = SyntaxNodeExtensions.ReplaceNode(root, blockFinder.ClassDeclarationNode, classDeclaration);
        }
        else
        {
            var theClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            var classDeclarationSyntax = theClass.AddMembers(outputDeclaration);
            Log.Info($"{classDeclarationSyntax}");
            return false;
        }

        var newSource = root.GetText().ToString();
        Log.Debug(newSource);

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, false, out _);
    }

    public static bool AdjustInputOrderOfSymbol(Symbol symbol)
    {
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{symbol.Name}' source.");
            return false;
        }

        var root = syntaxTree.GetRoot();

        var inputNodeFinder = new InputsAndOutputs.AllInputNodesFinder();
        root = inputNodeFinder.Visit(root);

        // Check if the order in code is the same as in symbol
        Debug.Assert(inputNodeFinder.InputNodesFound.Count == symbol.InputDefinitions.Count);
        var orderIsOk = true;
        for (int i = 0; i < inputNodeFinder.InputNodesFound.Count; i++)
        {
            if (inputNodeFinder.InputNodesFound[i].Item1 != symbol.InputDefinitions[i].Name)
            {
                orderIsOk = false;
                break;
            }
        }

        if (orderIsOk)
            return true; // nothing to do

        var inputDeclarations = new List<SyntaxNode>(symbol.InputDefinitions.Count);
        foreach (var inputDef in symbol.InputDefinitions)
        {
            var inputType = inputDef.DefaultValue.ValueType;
            var @namespace = inputType.Namespace;
            if (@namespace == "System")
                @namespace = String.Empty;
            else
                @namespace += ".";

            string? foundAttributeString = null;
            foreach (var (nodeName, syntaxNode) in inputNodeFinder.InputNodesFound)
            {
                if (nodeName != inputDef.Name)
                    continue;

                foundAttributeString = syntaxNode.ToString().Split("\n").First();
                break;
            }

            var fallbackAttributeString = "\n        [Input(Guid = \"" + inputDef.Id + "\")]\n";
            var attributeString = foundAttributeString == null
                                      ? fallbackAttributeString
                                      : $"\n        {foundAttributeString}\n";

            var typeName = TypeNameRegistry.Entries[inputType];
            var slotString = (inputDef.IsMultiInput ? "MultiInputSlot<" : "InputSlot<") + @namespace + typeName + ">";
            var inputString = "        public readonly " + slotString + " " + inputDef.Name + " = new " + slotString + "();\n";

            var code = attributeString + inputString;
            var inputDeclaration = SyntaxFactory.ParseMemberDeclaration(code);
            if (inputDeclaration == null)
            {
                Log.Error("Skipping invalid input declaration " + code);
                continue;
            }
            inputDeclarations.Add(inputDeclaration);
        }

        var replacer = new InputsAndOutputs.AllInputNodesReplacer
                           {
                               ReplacementNodes = inputDeclarations.ToArray()
                           };
        root = replacer.Visit(root);

        var newSource = root.GetText().ToString();
        Log.Debug(newSource);

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, true, out _);
    }
    
    
    public static bool RenameInput(Symbol symbol, Guid inputId, string newName, bool dryRun, out string? warning)
    {
        warning = null;

        var isValid = GraphUtils.IsNewFieldNameValid(newName, symbol, out var reason);
        if (!isValid)
        {
            warning= $"{newName} is not a valid input name: {reason}";
            return false;
        }
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            warning= $"Error getting syntax tree from symbol '{symbol.Name}' source.";
            return false;
        }
            
        var root = syntaxTree.GetRoot();
        var sourceCode = root.GetText().ToString();
        
        var alreadyExistsResult = Regex.Match(sourceCode, $@"\b{newName}\b");
        if (alreadyExistsResult.Success)
        {
            warning= $"{newName} is already used within the operators source code.";
            return false;
        }

        var inputDef = symbol.InputDefinitions.FirstOrDefault(i => i.Id == inputId);
        if (inputDef == null)
        {
            warning= $"{inputId} input definition not found.";
            return false;
        }

        var newSource = Regex.Replace(sourceCode, @$"\b{inputDef.Name}\b(?!\()", newName);

        if (dryRun)
            return true;

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, true, out warning);
    }

    public static bool RenameOutput(Symbol symbol, Guid outputId, string newName, bool dryRun, out string? warning)
    {
        warning = null;

        var isValid = GraphUtils.IsNewFieldNameValid(newName, symbol, out var reason);
        if (!isValid)
        {
            warning = $"{newName} is not a valid output name: {reason}";
            return false;
        }
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            warning = $"Error getting syntax tree from symbol '{symbol.Name}' source.";
            return false;
        }

        var root = syntaxTree.GetRoot();
        var sourceCode = root.GetText().ToString();

        var alreadyExistsResult = Regex.Match(sourceCode, $@"\b{newName}\b");
        if (alreadyExistsResult.Success)
        {
            warning = $"{newName} is already used within the operators source code.";
            return false;
        }

        var outputDef = symbol.OutputDefinitions.FirstOrDefault(o => o.Id == outputId);
        if (outputDef == null)
        {
            warning = $"{outputId} output definition not found.";
            return false;
        }

        var newSource = Regex.Replace(sourceCode, @$"\b{outputDef.Name}\b(?!\()", newName);

        if (dryRun)
            return true;

        return EditableSymbolProject.RecompileSymbol(symbol, newSource, true, out warning);
    }
}