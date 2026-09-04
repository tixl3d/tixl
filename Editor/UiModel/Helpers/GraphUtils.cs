#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using T3.Core.Model;
using T3.Core.Operator;

namespace T3.Editor.UiModel.Helpers;

internal static partial class GraphUtils
{
    public static SyntaxTree? GetSyntaxTree(Symbol symbol)
    {
        var package = (EditorSymbolPackage)symbol.SymbolPackage;
        if (package is not EditableSymbolProject project || !project.TryGetPendingSourceCode(symbol.Id, out var sourceCode))
        {
            // there's intermediate source, so use this
            if (!package.TryGetSourceCodePath(symbol, out var sourceCodePath))
            {
                Log.Error($"Could not find source file for symbol '{symbol.Name}'");
                return null;
            }

            try
            {
                sourceCode = File.ReadAllText(sourceCodePath!);
            }
            catch (Exception e)
            {
                Log.Error($"Error opening file '{sourceCodePath}");
                Log.Error(e.Message);
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            Log.Info("Source was empty, skip compilation.");
            return null;
        }

        return CSharpSyntaxTree.ParseText(sourceCode);
    }

    public static bool IsNewFieldNameValid(string? newFieldName, Symbol symbol, [NotNullWhen(false)] out string? reason)
    {
        if (string.IsNullOrWhiteSpace(newFieldName))
        {
            reason = "Name must not be empty";
            return false;
        }
        
        if (!IsIdentifierValidNotEmpty(newFieldName))
        {
            reason = "Name must be a valid C# identifier";
            return false;
        }
        
        var typeInfo = symbol.SymbolPackage.AssemblyInformation.OperatorTypeInfo[symbol.Id];
        if (typeInfo.MemberNames.Contains(newFieldName))
        {
            reason = "Name must be unique within the operator";
            return false;
        }
        
        reason = null;
        return true;
    }

    public static bool IsNewSymbolNameValid(string newSymbolName, string destinationNamespace, SymbolPackage destinationPackage, [NotNullWhen(false)] out string? reason)
    {
        var isNamespaceValid = IsNamespaceValid(destinationNamespace, false, out var namespaceComponents);
        if (!isNamespaceValid)
        {
            reason = "Namespace must be a valid C# namespace";
            return false;
        }

        if (!destinationNamespace.StartsWith(destinationPackage.RootNamespace))
        {
            reason = "Namespace must be within the project's root namespace";
            return false;
        }
        
        if(!IsIdentifierValid(newSymbolName))
        {
            reason = "Name must be a valid C# identifier";
            return false;
        }
        
        if (ReservedWords.Contains(newSymbolName))
        {
            reason = "Name must not be a C# reserved word";
            return false;
        }
        
        if (destinationPackage.ContainsSymbolName(newSymbolName, destinationNamespace))
        {
            reason = "Name must be unique within the namespace";
            return false;
        }

        // check if the namespace contains a class name that exists
        var namespaceSegmentCount = namespaceComponents.Length;
        Span<char> nspBuilderBuffer = stackalloc char[destinationNamespace.Length];
        for (int i = namespaceSegmentCount - 1; i >= 1; i--)
        {
            // treat the last segment of the namespace as a potential class name
            var className = namespaceComponents[i];
            
            var nspBuilderCount = 0;
            
            // build the namespace up to the current segment
            for(int nspIndex = 0; nspIndex < i; nspIndex++)
            {
                var nspSegment = namespaceComponents[nspIndex];
                var insertSlice = nspBuilderBuffer[nspBuilderCount..];
                nspSegment.CopyTo(insertSlice);
                nspBuilderCount += nspSegment.Length;
                nspBuilderBuffer[nspBuilderCount] = '.';
                nspBuilderCount++;
            }

            var builtNamespace = nspBuilderBuffer[..(nspBuilderCount - 1)]; // remove trailing '.'

            // check if the package contains namespace.className
            if (destinationPackage.ContainsSymbolName(className, builtNamespace))
            {
                reason = $"Namespace must not contain a class name '{builtNamespace}.{className}'";
                return false;
            }
        }
        
        var totalLength = destinationNamespace.Length + newSymbolName.Length + 1;
        const int maxPathLength = 255;
        if (totalLength > maxPathLength)
        {
            reason = $"Total length of namespace and class name must not exceed {maxPathLength} characters";
            return false;
        }
        
        reason = null;
        return true;
    }

    /// <summary>
    /// Converts a namespace to its source-code form by escaping reserved-word segments with '@'
    /// (e.g. Lib.numbers.float.basic → Lib.numbers.@float.basic).
    /// </summary>
    public static bool TryConvertToValidCodeNamespace(string sourceNamespace, out string result)
    {
        var parts = sourceNamespace.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (!IsIdentifierValid(part))
            {
                var newPart = "@" + part;
                if (!IsIdentifierValid(newPart))
                {
                    result = string.Empty;
                    return false;
                }

                parts[i] = newPart;
            }
        }

        result = string.Join('.', parts);
        return true;
    }

    public static bool IsIdentifierValid(string? className) => !string.IsNullOrWhiteSpace(className)
                                                               && IsIdentifierValidNotEmpty(className);
    
    private static bool IsIdentifierValidNotEmpty(string className)
    {
        var isEscaped = className.StartsWith('@');
        var identifier = isEscaped ? className[1..] : className;
        if (!SyntaxFacts.IsValidIdentifier(identifier))
            return false;

        // Reserved keywords are only usable as identifiers when @-escaped (e.g. Lib.numbers.@float)
        return isEscaped || SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None;
    }

    public static bool IsValidProjectName(string userName) => IsIdentifierValid(userName);

    public static bool IsNamespaceValid(string namespaceName, bool needsToBeUnique, out string[] namespaceComponents)
    {
        if (namespaceName == "")
        {
            namespaceComponents = [];
            return true;
        }
        namespaceComponents = namespaceName.Split('.');
        return ValidTypeNameSpacePattern.IsMatch(namespaceName)
               && !namespaceComponents.Any(x => x.Length == 0
                                                || ReservedWords.Contains(x)
                                           /* || char.IsLower(x[0])*/) // enforce PascalCase
               && (!needsToBeUnique
                   || !SymbolPackage.AllPackages.Any(x => x.OwnsNamespace(namespaceName) // namespace is already owned by another package
                                                          || x.RootNamespace.StartsWith(namespaceName))); // namespace is the name of a larger namespace the package owns a part of - this is not allowed
    }

    private static readonly Regex ValidTypeNameSpacePattern = NamespaceRegex();
    private static readonly string[] ReservedWords = new string[] { "value", "Var", "instance", "item", "Input", "slot" };

    [GeneratedRegex(@"^([A-Za-z][A-Za-z\d]*)(\.([A-Za-z_][A-Za-z_\d]*))*$")]
    private static partial Regex NamespaceRegex();
}