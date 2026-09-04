using System.Text;

namespace ProjectUpdater;

internal sealed partial class Conversion
{
    private FileChangeInfo ConvertAndMoveCSharp(string file, string text, string originalRootDirectory, string newRootDirectory)
    {
        StringBuilder stringBuilder = new(32);

        var folderComponents = Utils.GetSubfolderArray(file, originalRootDirectory, stringBuilder);
        stringBuilder.Clear();

        Utils.ConvertLineEndingsOf(ref text, false);
        var gotGuid = TryConvertToModernOperator(ref text, out var guid);
        var namespaceChanged = ChangeNamespaceTo(ref text, folderComponents, stringBuilder, out var oldNamespace, out var newNamespace);

        if (namespaceChanged)
        {
            lock (_changedNamespaces)
                _changedNamespaces.Add(new NamespaceChanged(oldNamespace, newNamespace));
        }
        
        stringBuilder.Clear();
        
        ConvertCode(ref text);

        string newFileDirectory = Path.Combine(folderComponents);
        newFileDirectory = Path.Combine(newRootDirectory, newFileDirectory);

        if(gotGuid)
            _destinationDirectories[guid] = newFileDirectory;

        var newFilePath = Path.Combine(newFileDirectory, Path.GetFileName(file));

        return new FileChangeInfo(newFilePath, text);
    }

    /// <summary>
    /// Converts code to new format, searching for things that would not work with new changes and replacing them
    /// </summary>
    private static void ConvertCode(ref string code)
    {
        const string vec = "Vector";
        const string vecReplacement = "System.Numerics.Vector";

        const string shaderSuffix = "D3D";
        const string pixelShader = "PixelShader";
        const string vertexShader = "VertexShader";
        const string computeShader = "ComputeShader";

        const string pixelShaderReplacement = pixelShader + shaderSuffix;
        const string vertexShaderReplacement = vertexShader + shaderSuffix;
        const string computeShaderReplacement = computeShader + shaderSuffix;

        const string classFmt = "class {0}";

        var vecClassDecl = string.Format(classFmt, vec);
        if (!code.Contains(vecClassDecl))
        {
            Replace(ref code, vec, vecReplacement,
                isValidRemoval: (codeString, index) =>
                {
                    var isNotPrecededByDot = codeString[index - 1] != '.';
                    
                    var nextChar = codeString[index + vec.Length];
                    var isFollowedByNumber = char.IsDigit(nextChar);
                    var nextNextChar = codeString[index + vec.Length + 1];
                    var isAccessedAsType = nextNextChar != '.' && nextNextChar != '>' && nextNextChar != ' ';

                    const string usingString = "using ";
                    var previousSpan = codeString.AsSpan(index - usingString.Length, usingString.Length);
                    var isPrecededByUsing = previousSpan.StartsWith(usingString);
                    var isPrecededByLetter = char.IsLetterOrDigit(codeString[index - 1]);
                    return isNotPrecededByDot && isFollowedByNumber && !isPrecededByUsing && isAccessedAsType && !isPrecededByLetter;
                });
        }

        var pixelShaderClassDecl = string.Format(classFmt, pixelShader);
        if (!code.Contains(pixelShaderClassDecl))
        {
            var replaced = Replace(ref code, pixelShader, pixelShaderReplacement, CanReplaceShaderReference);
            if(replaced)
                AddShaderUsingStatement(ref code, pixelShader, pixelShaderReplacement);
        }

        var vertexShaderClassDecl = string.Format(classFmt, vertexShader);
        if (!code.Contains(vertexShaderClassDecl))
        {
            var replaced = Replace(ref code, vertexShader, vertexShaderReplacement, CanReplaceShaderReference);
            if(replaced)
                AddShaderUsingStatement(ref code, vertexShader, vertexShaderReplacement);
        }

        var computeShaderClassDecl = string.Format(classFmt, computeShader);
        if (!code.Contains(computeShaderClassDecl))
        {
            var replaced = Replace(ref code, computeShader, computeShaderReplacement, CanReplaceShaderReference);
            
            if(replaced)
                AddShaderUsingStatement(ref code, computeShader, computeShaderReplacement);
        }

        Replace(ref code, "Core.DataTypes.", "T3.Core.DataTypes.", (code, index) =>
        {
            char precedingChar = code[index - 1];
            return precedingChar != '.';
        });

        code = code.Replace("new PointLight", "new T3.Core.Rendering.PointLight");

        return;

        static bool CanReplaceShaderReference(string codeString, int index)
        {
            var alreadyReplaced = codeString[index..(index + 3)] != shaderSuffix;
            var precededByDot = codeString[index - 1] == '.';

            return !alreadyReplaced && !precededByDot;
        }

        static void AddShaderUsingStatement(ref string s, string shaderTypeName, string replacement)
        {
            const string shaderUsingStatementFmt = "using {0} = T3.Core.DataTypes.{1};";
            var usingStatement = string.Format(shaderUsingStatementFmt, replacement, shaderTypeName);

            if (!s.Contains(usingStatement))
                s = usingStatement + Environment.NewLine + s;
        }
        
        // returns true if something was replaced;
        static bool Replace(ref string code, string term, string termReplacement,
            Func<string, int, bool>? isValidRemoval = null)
        {
            int startIndex = 0;
            int foundIndex = code.IndexOf(term, startIndex, StringComparison.Ordinal);
            bool wasReplaced = false;
            while (foundIndex != -1)
            {
                if (isValidRemoval != null && !isValidRemoval(code, foundIndex))
                {
                    startIndex = foundIndex + term.Length;
                    foundIndex = code.IndexOf(term, startIndex, StringComparison.Ordinal);
                    continue;
                }

                // replace
                code = code.Remove(foundIndex, term.Length);
                code = code.Insert(foundIndex, termReplacement);

                startIndex = foundIndex + termReplacement.Length;
                foundIndex = code.IndexOf(term, startIndex, StringComparison.Ordinal);

                wasReplaced = true;
            }
            
            return wasReplaced;
        }
    }

    /// <summary>
    /// Returns true if the text namespace was changed
    /// </summary>
    private bool ChangeNamespaceTo(ref string text, string[] namespaceComponents, StringBuilder sb, out string oldNamespace, out string newNamespace)
    {
        var namespaceStartIndex = text.IndexOf(NamespacePrefix, StringComparison.Ordinal);

        if (namespaceStartIndex == -1)
        {
            oldNamespace = string.Empty;
            newNamespace = string.Empty;
            return false;
        }

        for (int i = 0; i < namespaceComponents.Length; i++)
        {
            if (i != 0)
            {
                sb.Append('.');
            }

            var namespaceComponent = namespaceComponents[i];

            if (!CodeDomProvider.IsValidIdentifier(namespaceComponent))
            {
                if (char.IsLetter(namespaceComponent[0]))
                    namespaceComponent = '@' + namespaceComponent;
                else if (namespaceComponent[0] != '_')
                    namespaceComponent = '_' + namespaceComponent;

                namespaceComponent = namespaceComponent.Replace('-', '_');

                if (!CodeDomProvider.IsValidIdentifier(namespaceComponent))
                    namespaceComponent = CodeDomProvider.CreateValidIdentifier(namespaceComponent);
            }

            namespaceComponents[i] = namespaceComponent;
            sb.Append(namespaceComponent);
        }

        newNamespace = sb.ToString();
        var namespaceDeclaration = $"namespace {newNamespace}";
        var namespaceEndIndex = text.IndexOf('\n', namespaceStartIndex);
        oldNamespace = text.AsSpan(namespaceStartIndex, namespaceEndIndex - namespaceStartIndex).ToString();
        
        if (text.AsSpan(namespaceStartIndex, namespaceEndIndex - namespaceStartIndex + 1).Contains(';'))
        {
            namespaceDeclaration += ';';
        }

        text = text
            .Remove(namespaceStartIndex, namespaceEndIndex - namespaceStartIndex)
            .Insert(namespaceStartIndex, namespaceDeclaration);

        sb.Clear();

        return true;
    }

    private static bool TryConvertToModernOperator(ref string text, out Guid guid)
    {
        int prefixLength = DeprecatedNamespacePrefix.Length;
        var prefixIndex = text.IndexOf(DeprecatedNamespacePrefix, StringComparison.Ordinal);

        if (prefixIndex == -1)
        {
            const string guidStart = "[Guid(\"";
            var guidKeyStartIndex = text.IndexOf(guidStart, StringComparison.Ordinal);
            if (guidKeyStartIndex == -1)
            {
                guid = Guid.Empty;
                return false;
            }

            var guidStartIndex = guidKeyStartIndex + guidStart.Length;
            var guidText = text.Substring(guidStartIndex, 36);
            var gotGuid = Guid.TryParse(guidText, out guid);
            
            if(!gotGuid)
            {
                Console.WriteLine($"Could not parse guid in \"{guidText}\"");
                guid = Guid.Empty;
            }

            return gotGuid;
        }

        var guidIndex = prefixIndex + prefixLength;
        var guidStr = text.Substring(guidIndex, 36).Replace('_', '-');
        
        const string guidAttributeFormat = "\n\t[Guid(\"{0}\")]";
        string newGuidAttribute = string.Format(guidAttributeFormat, guidStr);

        var bracketIndex = text.IndexOf('{', guidIndex);
        text = text.Insert(bracketIndex + 1, newGuidAttribute);

        _ = AddInteropUsingIfNecessary(ref text);
        
        var success = Guid.TryParse(guidStr, out guid);

        if (!success)
        {
            Console.WriteLine($"Failed to replace guid in \"{guidStr}\"");
        }
        
        return success;

        static bool AddInteropUsingIfNecessary(ref string text)
        {
            const string usingStatement = "using System.Runtime.InteropServices;";
            const string usingStatementWithNewLine = "using System.Runtime.InteropServices;\n";
            if (text.Contains(usingStatement))
            {
                return false;
            }

            text = usingStatementWithNewLine + text;
            return false;
        }
    }

    public record struct NamespaceChanged(string OldNamespace, string NewNamespace);
}