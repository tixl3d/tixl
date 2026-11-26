#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using GraphUtils = T3.Editor.UiModel.Helpers.GraphUtils;

namespace T3.Editor.UiModel.Modification;

/// <summary>
/// Enums that int inputs map their values to (<see cref="InputAttribute.MappedType"/>): carrying them over when
/// publishing an input, and editing the ones an operator declares inside its own class.
/// </summary>
internal static partial class InputsAndOutputs
{
    internal readonly record struct EnumEntry(string Name, int Value);

    public static bool TryGetMappedEnumType(Symbol symbol, Guid inputId, [NotNullWhen(true)] out Type? enumType)
    {
        const BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var field in symbol.InstanceType.GetFields(fieldFlags))
        {
            var attribute = field.GetCustomAttribute<InputAttribute>();
            if (attribute == null || attribute.Id != inputId)
                continue;

            enumType = attribute.MappedType is { IsEnum: true } mappedType ? mappedType : null;
            return enumType != null;
        }

        enumType = null;
        return false;
    }

    /** In declaration order, which is also the order dropdowns list them in. */
    public static EnumEntry[] GetEnumEntries(Type enumType)
    {
        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
        var entries = new EnumEntry[fields.Length];
        for (var index = 0; index < fields.Length; index++)
        {
            var rawValue = fields[index].GetRawConstantValue();
            var value = rawValue is ulong unsignedValue ? unchecked((long)unsignedValue) : Convert.ToInt64(rawValue, CultureInfo.InvariantCulture);
            entries[index] = new EnumEntry(fields[index].Name, unchecked((int)value));
        }

        return entries;
    }

    /// <summary>
    /// True if the operator's class holds more than its slots and nested enums, e.g. an update method that might use
    /// the enum's entry names.
    /// </summary>
    public static bool HasCustomCode(Symbol symbol)
    {
        var classNode = FindClassDeclaration(symbol);
        if (classNode == null || classNode.Modifiers.Any(SyntaxKind.PartialKeyword))
            return true;

        foreach (var member in classNode.Members)
        {
            if (member is EnumDeclarationSyntax)
                continue;

            if (member is FieldDeclarationSyntax field && IsSlotField(field))
                continue;

            return true;
        }

        return false;

        static bool IsSlotField(FieldDeclarationSyntax field)
        {
            foreach (var attributeList in field.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var name = attribute.Name.ToString();
                    if (name is "Input" or "Output" or "InputAttribute" or "OutputAttribute")
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Replaces the entries of an enum declared in the operator's class and recompiles. Attributes, modifiers and the
    /// underlying type of the declaration are kept.
    /// </summary>
    /// <param name="error">The compile error, to show next to the edit that caused it.</param>
    public static bool TryReplaceEnumEntries(Symbol symbol, string enumName, IReadOnlyList<EnumEntry> entries, [NotNullWhen(false)] out string? error)
    {
        var classNode = FindClassDeclaration(symbol);
        if (classNode == null)
        {
            error = $"Can't find the class declaration of {symbol.Name}.";
            return false;
        }

        EnumDeclarationSyntax? enumDeclaration = null;
        foreach (var member in classNode.Members)
        {
            if (member is EnumDeclarationSyntax declaration && declaration.Identifier.ValueText == enumName)
            {
                enumDeclaration = declaration;
                break;
            }
        }

        if (enumDeclaration == null)
        {
            error = $"{symbol.Name} doesn't declare an enum {enumName}.";
            return false;
        }

        var source = classNode.SyntaxTree.GetText().ToString();
        var column = classNode.SyntaxTree.GetLineSpan(enumDeclaration.Span).StartLinePosition.Character;
        var indent = new string(' ', column);

        var sb = new StringBuilder();
        sb.Append(source, 0, enumDeclaration.OpenBraceToken.SpanStart);
        sb.Append("{\n");
        foreach (var entry in entries)
        {
            sb.Append(indent).Append("    ").Append(EscapeKeyword(entry.Name))
              .Append(" = ").Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        }

        sb.Append(indent).Append('}');
        sb.Append(source, enumDeclaration.CloseBraceToken.Span.End, source.Length - enumDeclaration.CloseBraceToken.Span.End);

        if (EditableSymbolProject.RecompileSymbol(symbol, sb.ToString(), true, out var reason, showFailureDialog: false))
        {
            error = null;
            return true;
        }

        error = reason ?? "Compilation failed.";
        return false;
    }

    /** A free name for an enum nested in the operator's class, derived from the input it is for. */
    public static string GetNameForNewEnum(Symbol symbol, string inputName)
    {
        var source = GraphUtils.GetSyntaxTree(symbol)?.GetRoot().ToFullString() ?? string.Empty;
        var baseName = inputName.EndsWith('s') ? inputName + "Options" : inputName + "s";
        var name = baseName;
        for (var suffix = 2; !IsNestedTypeNameFree(name, symbol, inputName, source); suffix++)
        {
            name = baseName + suffix;
        }

        return name;
    }

    /// <summary>
    /// Declares an enum in the operator's class and maps the int input to it, so the parameter shows its entries.
    /// </summary>
    public static bool TryAddMappedEnum(Symbol symbol, Guid inputId, string enumName, IReadOnlyList<EnumEntry> entries,
                                        [NotNullWhen(false)] out string? error)
    {
        var root = GraphUtils.GetSyntaxTree(symbol)?.GetRoot();
        if (root == null || !TryFindInputAttribute(root, inputId, out var attribute))
        {
            error = $"Can't find the input {inputId} in the source of {symbol.Name}.";
            return false;
        }

        root = root.ReplaceNode(attribute, WithMappedTypeArgument(attribute, enumName));

        var classFinder = new ClassDeclarationFinder(symbol.Name);
        classFinder.Visit(root);
        if (classFinder.ClassDeclarationNode == null)
        {
            error = $"Can't find the class declaration of {symbol.Name}.";
            return false;
        }

        var memberNames = new string[entries.Count];
        var memberValues = new string[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            memberNames[index] = entries[index].Name;
            memberValues[index] = entries[index].Value.ToString(CultureInfo.InvariantCulture);
        }

        var enumDeclaration = SyntaxFactory.ParseMemberDeclaration(BuildEnumSource(enumName, string.Empty, false, memberNames, memberValues));
        if (enumDeclaration == null)
        {
            error = $"Invalid enum declaration {enumName}.";
            return false;
        }

        var classNode = classFinder.ClassDeclarationNode;
        root = root.ReplaceNode(classNode, classNode.WithMembers(classNode.Members.Insert(0, enumDeclaration)));
        return TryRecompileQuietly(symbol, root, out error);
    }

    /// <summary>
    /// Shows the int input as a plain value again. The enum it was mapped to is removed too if it is declared in the
    /// operator's class and nothing else there refers to it.
    /// </summary>
    public static bool TryRemoveMappedEnum(Symbol symbol, Guid inputId, [NotNullWhen(false)] out string? error)
    {
        var root = GraphUtils.GetSyntaxTree(symbol)?.GetRoot();
        if (root == null || !TryFindInputAttribute(root, inputId, out var attribute))
        {
            error = $"Can't find the input {inputId} in the source of {symbol.Name}.";
            return false;
        }

        if (!TryGetMappedTypeArgument(attribute, out var mappedTypeArgument))
        {
            error = null;
            return true;
        }

        var enumName = mappedTypeArgument.Expression is TypeOfExpressionSyntax typeOf ? typeOf.Type.ToString() : null;
        root = root.ReplaceNode(attribute, WithMappedTypeArgument(attribute, null));

        if (enumName != null && !IsIdentifierReferenced(root, enumName))
            root = RemoveNestedEnum(root, symbol.Name, enumName);

        return TryRecompileQuietly(symbol, root, out error);

        static bool IsIdentifierReferenced(SyntaxNode root, string name)
        {
            foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Identifier.ValueText == name)
                    return true;
            }

            return false;
        }
    }

    public static bool IsValidEnumEntryName(string name)
    {
        return SyntaxFacts.IsValidIdentifier(name) && !name.StartsWith("__", StringComparison.Ordinal);
    }

    private static bool TryFindInputAttribute(SyntaxNode root, Guid inputId, [NotNullWhen(true)] out AttributeSyntax? inputAttribute)
    {
        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var name = attribute.Name.ToString();
            if (name is not ("Input" or "InputAttribute") || attribute.ArgumentList == null)
                continue;

            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals?.Name.Identifier.ValueText == "Guid"
                    && argument.Expression is LiteralExpressionSyntax literal
                    && Guid.TryParse(literal.Token.ValueText, out var id)
                    && id == inputId)
                {
                    inputAttribute = attribute;
                    return true;
                }
            }
        }

        inputAttribute = null;
        return false;
    }

    private static bool TryGetMappedTypeArgument(AttributeSyntax attribute, [NotNullWhen(true)] out AttributeArgumentSyntax? mappedTypeArgument)
    {
        if (attribute.ArgumentList != null)
        {
            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals?.Name.Identifier.ValueText == nameof(InputAttribute.MappedType))
                {
                    mappedTypeArgument = argument;
                    return true;
                }
            }
        }

        mappedTypeArgument = null;
        return false;
    }

    /** Replaces or removes (for a null name) the attribute's MappedType argument, keeping the others as written. */
    private static AttributeSyntax WithMappedTypeArgument(AttributeSyntax attribute, string? enumName)
    {
        var arguments = new StringBuilder();
        if (attribute.ArgumentList != null)
        {
            foreach (var argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals?.Name.Identifier.ValueText == nameof(InputAttribute.MappedType))
                    continue;

                if (arguments.Length > 0)
                    arguments.Append(", ");

                arguments.Append(argument.ToString());
            }
        }

        if (enumName != null)
        {
            if (arguments.Length > 0)
                arguments.Append(", ");

            arguments.Append("MappedType = typeof(").Append(enumName).Append(')');
        }

        return attribute.WithArgumentList(SyntaxFactory.ParseAttributeArgumentList("(" + arguments + ")"));
    }

    private static bool TryRecompileQuietly(Symbol symbol, SyntaxNode root, [NotNullWhen(false)] out string? error)
    {
        if (EditableSymbolProject.RecompileSymbol(symbol, root.GetText().ToString(), true, out var reason, showFailureDialog: false))
        {
            error = null;
            return true;
        }

        error = reason ?? "Compilation failed.";
        return false;
    }

    private static ClassDeclarationSyntax? FindClassDeclaration(Symbol symbol)
    {
        var syntaxTree = GraphUtils.GetSyntaxTree(symbol);
        if (syntaxTree == null)
            return null;

        var classFinder = new ClassDeclarationFinder(symbol.Name);
        classFinder.Visit(syntaxTree.GetRoot());
        return classFinder.ClassDeclarationNode;
    }

    private static string EscapeKeyword(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
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

            if (existingType == null && IsNestedTypeNameFree(name, symbol, inputName, source))
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

        var memberNames = Enum.GetNames(enumType);
        var values = Enum.GetValuesAsUnderlyingType(enumType);
        var memberValues = new string[memberNames.Length];
        for (var index = 0; index < memberNames.Length; index++)
        {
            memberValues[index] = Convert.ToString(values.GetValue(index), CultureInfo.InvariantCulture)!;
        }

        return BuildEnumSource(name, baseType, enumType.IsDefined(typeof(FlagsAttribute), false), memberNames, memberValues);
    }

    private static string BuildEnumSource(string name, string baseType, bool isFlagEnum, string[] memberNames, string[] memberValues)
    {
        var sb = new StringBuilder();
        sb.Append('\n');
        if (isFlagEnum)
            sb.Append("    [System.Flags]\n");

        sb.Append("    private enum ").Append(name).Append(baseType).Append("\n    {\n");
        for (var index = 0; index < memberNames.Length; index++)
        {
            sb.Append("        ").Append(EscapeKeyword(memberNames[index])).Append(" = ").Append(memberValues[index]).Append(",\n");
        }

        sb.Append("    }\n\n");
        return sb.ToString();
    }

    /** A nested type shadows any outer type of that name the class might already use, so every name in the source is avoided. */
    private static bool IsNestedTypeNameFree(string name, Symbol symbol, string inputName, string source)
    {
        return name != symbol.Name
               && name != inputName
               && GraphUtils.IsNewFieldNameValid(name, symbol, out _)
               && !Regex.IsMatch(source, $@"\b{Regex.Escape(name)}\b");
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
}
