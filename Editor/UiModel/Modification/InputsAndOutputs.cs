#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
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
    /// <param name="nestedEnumToRemove">An enum copied into the class by <see cref="AddInputToSymbol(Guid,string,bool,Type,Symbol,Type?,out string?)"/>.</param>
    public static bool RemoveInputsAndOutputsFromSymbol(Guid[] inputIdsToRemove, Guid[] outputIdsToRemove, Symbol symbol, string? nestedEnumToRemove = null)
    {
        if (inputIdsToRemove.Length == 0 && outputIdsToRemove.Length == 0 && nestedEnumToRemove == null)
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

        if (nestedEnumToRemove != null)
            root = RemoveNestedEnum(root, symbol.Name, nestedEnumToRemove);

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

    /// <summary>
    /// Finds the operator class by name. Source files can hold further classes, e.g. a project's
    /// home symbol also defines its <c>ShareDefinition</c>.
    /// </summary>
    private sealed class ClassDeclarationFinder(string className) : CSharpSyntaxWalker
    {
        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (node.Identifier.ValueText == className)
            {
                ClassDeclarationNode = node;
                return;
            }

            base.VisitClassDeclaration(node);
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
        return AddInputToSymbol(inputId, inputName, multiInput, inputType, symbol, null, out _);
    }

    /// <summary>
    /// Adds an input to the symbol's source and recompiles it.
    /// </summary>
    /// <param name="mappedEnumType">
    /// Shows an int input as an enum dropdown. The attribute references the enum if the symbol's source can reach it,
    /// otherwise a copy is added to the class. If that doesn't compile, the input is added without it.
    /// </param>
    /// <param name="copiedEnumName">The enum added to the class, so undo can remove it again.</param>
    public static bool AddInputToSymbol(Guid inputId, string inputName, bool multiInput, Type inputType, Symbol symbol,
                                        Type? mappedEnumType, out string? copiedEnumName)
    {
        copiedEnumName = null;
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
        {
            Log.Error($"Error getting syntax tree from symbol '{symbol.Name}' source.");
            return false;
        }

        var root = syntaxTree.GetRoot();
        if (!TryBuildSourceWithInput(root, inputId, inputName, multiInput, inputType, symbol, null, null, out var plainSource))
            return false;

        if (mappedEnumType is { IsEnum: true } && inputType == typeof(int) && !multiInput
            && TryGetMappedTypeReference(mappedEnumType, symbol, inputName, root.ToFullString(), out var typeReference, out var enumCopy)
            && TryBuildSourceWithInput(root, inputId, inputName, multiInput, inputType, symbol, typeReference, enumCopy, out var mappedSource)
            && !HasSyntaxErrors(mappedSource))
        {
            if (EditableSymbolProject.RecompileSymbol(symbol, mappedSource, false, out var reason, showFailureDialog: false))
            {
                copiedEnumName = enumCopy != null ? typeReference : null;
                return true;
            }

            Log.Warning($"Adding '{inputName}' without enum {mappedEnumType.Name}, because the source didn't compile: {reason}");
        }

        return EditableSymbolProject.RecompileSymbol(symbol, plainSource, false, out _);
    }

    private static bool TryBuildSourceWithInput(SyntaxNode root, Guid inputId, string inputName, bool multiInput, Type inputType, Symbol symbol,
                                                string? mappedTypeReference, MemberDeclarationSyntax? enumCopy,
                                                [NotNullWhen(true)] out string? newSource)
    {
        newSource = null;
        var inputNodeFinder = new InputNodeByTypeFinder();
        root = inputNodeFinder.Visit(root);

        var blockFinder = new ClassDeclarationFinder(symbol.Name);
        if (inputNodeFinder.LastInputNodeFound == null)
        {
            blockFinder.Visit(root);
            if (blockFinder.ClassDeclarationNode == null)
            {
                Log.Error($"Can't find class declaration of '{symbol.Name}'.");
                return false;
            }
        }

        var @namespace = inputType.Namespace;
        if (@namespace == "System")
            @namespace = String.Empty;
        else
            @namespace += ".";
        var mappedTypeArgument = mappedTypeReference == null ? string.Empty : $", MappedType = typeof({mappedTypeReference})";
        var attributeString = "\n        [Input(Guid = \"" + inputId + "\"" + mappedTypeArgument + ")]\n";
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

        if (enumCopy != null)
        {
            var classFinder = new ClassDeclarationFinder(symbol.Name);
            classFinder.Visit(root);
            if (classFinder.ClassDeclarationNode == null)
                return false;

            var classNode = classFinder.ClassDeclarationNode;
            root = root.ReplaceNode(classNode, classNode.WithMembers(classNode.Members.Insert(0, enumCopy)));
        }

        newSource = root.GetText().ToString();
        Log.Debug(newSource);
        return true;
    }

    /// <summary>
    /// Returns how the symbol's source can name the enum: directly if it is reachable from the symbol's assembly,
    /// or as a copy nested in the symbol's class, reusing an identical copy from an earlier publish.
    /// </summary>
    private static bool TryGetMappedTypeReference(Type enumType, Symbol symbol, string inputName, string source,
                                                  [NotNullWhen(true)] out string? typeReference, out MemberDeclarationSyntax? enumCopy)
    {
        enumCopy = null;
        if (IsTypeReachableFrom(enumType, symbol))
        {
            typeReference = "global::" + enumType.FullName!.Replace('+', '.');
            return true;
        }

        const BindingFlags nestedTypeFlags = BindingFlags.Public | BindingFlags.NonPublic;
        var baseName = enumType.Name;
        var name = baseName;
        for (var suffix = 2;; suffix++)
        {
            var existingType = symbol.InstanceType.GetNestedType(name, nestedTypeFlags);
            if (existingType != null && HaveSameMembers(existingType, enumType))
            {
                typeReference = name;
                return true;
            }

            // A nested type shadows any outer type of that name the class might already use, so avoid every name in the source.
            var isNameFree = existingType == null
                             && name != symbol.Name
                             && name != inputName
                             && GraphUtils.IsNewFieldNameValid(name, symbol, out _)
                             && !Regex.IsMatch(source, $@"\b{Regex.Escape(name)}\b");
            if (isNameFree)
                break;

            name = baseName + suffix;
        }

        enumCopy = SyntaxFactory.ParseMemberDeclaration(BuildEnumCopySource(enumType, name));
        typeReference = name;
        return enumCopy != null;
    }

    private static bool IsTypeReachableFrom(Type type, Symbol symbol)
    {
        if (type.FullName == null)
            return false;

        var symbolAssembly = symbol.InstanceType.Assembly;
        var typeAssemblyName = type.Assembly.GetName().Name;
        var isSameAssembly = typeAssemblyName == symbolAssembly.GetName().Name;
        if (!isSameAssembly && !IsAssemblyReferenced(symbolAssembly, typeAssemblyName))
            return false;

        for (var t = type; t != null; t = t.DeclaringType)
        {
            if (t.IsGenericType)
                return false;

            var isPublic = t.IsPublic || t.IsNestedPublic;
            var isInternal = t.IsNotPublic || t.IsNestedAssembly || t.IsNestedFamORAssem;
            if (!isPublic && !(isSameAssembly && isInternal))
                return false;
        }

        return true;

        static bool IsAssemblyReferenced(Assembly assembly, string? assemblyName)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name == assemblyName)
                    return true;
            }

            return false;
        }
    }

    private static bool HaveSameMembers(Type a, Type b)
    {
        if (!a.IsEnum
            || Enum.GetUnderlyingType(a) != Enum.GetUnderlyingType(b)
            || a.IsDefined(typeof(FlagsAttribute), false) != b.IsDefined(typeof(FlagsAttribute), false))
            return false;

        var namesA = Enum.GetNames(a);
        var namesB = Enum.GetNames(b);
        var valuesA = Enum.GetValuesAsUnderlyingType(a);
        var valuesB = Enum.GetValuesAsUnderlyingType(b);
        if (namesA.Length != namesB.Length)
            return false;

        for (var index = 0; index < namesA.Length; index++)
        {
            if (namesA[index] != namesB[index] || !Equals(valuesA.GetValue(index), valuesB.GetValue(index)))
                return false;
        }

        return true;
    }

    private static string BuildEnumCopySource(Type enumType, string name)
    {
        var underlyingType = Enum.GetUnderlyingType(enumType);
        var baseType = Type.GetTypeCode(underlyingType) switch
                           {
                               TypeCode.Byte   => " : byte",
                               TypeCode.SByte  => " : sbyte",
                               TypeCode.Int16  => " : short",
                               TypeCode.UInt16 => " : ushort",
                               TypeCode.UInt32 => " : uint",
                               TypeCode.Int64  => " : long",
                               TypeCode.UInt64 => " : ulong",
                               _               => string.Empty,
                           };

        var sb = new StringBuilder();
        sb.Append('\n');
        if (enumType.IsDefined(typeof(FlagsAttribute), false))
            sb.Append("    [System.Flags]\n");

        sb.Append("    private enum ").Append(name).Append(baseType).Append("\n    {\n");

        var memberNames = Enum.GetNames(enumType);
        var values = Enum.GetValuesAsUnderlyingType(enumType);
        for (var index = 0; index < memberNames.Length; index++)
        {
            var memberName = memberNames[index];
            if (SyntaxFacts.GetKeywordKind(memberName) != SyntaxKind.None)
                memberName = "@" + memberName;

            var value = Convert.ToString(values.GetValue(index), CultureInfo.InvariantCulture);
            sb.Append("        ").Append(memberName).Append(" = ").Append(value).Append(",\n");
        }

        sb.Append("    }\n\n");
        return sb.ToString();
    }

    private static bool HasSyntaxErrors(string source)
    {
        foreach (var diagnostic in CSharpSyntaxTree.ParseText(source).GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                return true;
        }

        return false;
    }

    private static SyntaxNode RemoveNestedEnum(SyntaxNode root, string className, string enumName)
    {
        var classFinder = new ClassDeclarationFinder(className);
        classFinder.Visit(root);
        if (classFinder.ClassDeclarationNode == null)
            return root;

        foreach (var member in classFinder.ClassDeclarationNode.Members)
        {
            if (member is EnumDeclarationSyntax enumDeclaration && enumDeclaration.Identifier.ValueText == enumName)
                return root.RemoveNode(enumDeclaration, SyntaxRemoveOptions.KeepNoTrivia) ?? root;
        }

        return root;
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
        
        var blockFinder = new InputsAndOutputs.ClassDeclarationFinder(symbol.Name);
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