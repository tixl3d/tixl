using System.IO;
using System.Text;
using T3.Core.Operator;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.UiHelpers.Wiki;

/// <summary>
/// Exports a folder structure with a complete wiki documentation for all operators in lib 
/// </summary>
public static class ExportWikiDocumentation
{

    public static void ExportWiki()
    {
        //var treeNode = new NamespaceTreeNode(NamespaceTreeNode.RootNodeId);
        //treeNode.PopulateCompleteTree();

        var toc = new List<SymbolUi>();

        foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
        {
            try
            {
                Symbol symbol = symbolUi.Symbol;
                if (!symbol.Namespace.StartsWith("Lib."))
                    continue;

                if (symbol.Name.StartsWith("_"))
                {
                    Log.Debug($"Skipping internal '{symbol.Name}'");
                    continue;
                }

                if (symbol.Namespace.Contains("._"))
                {
                    Log.Debug($"Skipping internal namespace '{symbol.Namespace}'");
                    continue;
                }
                    
                    
                // if (string.IsNullOrWhiteSpace(symbolUi.Description))
                // {
                //     symbolUi.Description = DescriptionMissing;
                // }
                    
                toc.Add(symbolUi);

                var folder = WikiOperatorsFolder;
                if (!Directory.Exists(folder))
                {
                    Log.Debug($" Creating folder {folder}");
                    Directory.CreateDirectory(folder);
                }
                    
                var filepath = Path.Combine(folder, GetWikiLinkForSymbol(symbol) + ".md");
                Log.Debug($" writing {filepath}...");

                WriteDocFile(filepath, symbolUi);
            }
            catch(Exception e)
            {
                Log.Error($" can't save wiki for {symbolUi.Symbol.Name}: {e.Message}");
            }
        }
            
        // Write table of contents overview
        try
        {
            var indexFilePath = Path.Combine(WikiOperatorsFolder, "lib.md");
            var indexFile = File.CreateText(indexFilePath);
            var toc2 = toc.OrderBy(t => t.Symbol.Namespace).ThenBy(t => t.Symbol.Name).ToList();

            var lastNamespace = string.Empty;
                
            foreach (var symbolUi in toc2)
            {
                var symbolNamespace = symbolUi.Symbol.Namespace;
                if (symbolNamespace != lastNamespace)
                {
                    indexFile.WriteLine($"## {symbolNamespace}");
                    lastNamespace = symbolNamespace;
                }
                var shortDescription = symbolUi.Description != DescriptionMissing 
                                           ? " -- " + symbolUi.Description.Split("\n").First()
                                           : string.Empty;
                indexFile.WriteLine($"- {symbolNamespace} [__{symbolUi.Symbol.Name}__]({GetWikiLinkForSymbol(symbolUi.Symbol)}) {shortDescription}");
            }

            indexFile.Close();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to write index file: {e.Message}");
        }
    }

    private static string GetWikiLinkForSymbol(Symbol symbol)
    {
        var path2 = $"{symbol.Namespace}.{symbol.Name}";
        return path2;
    }

    private static void WriteDocFile(string filepath, SymbolUi symbolUi)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#  __{symbolUi.Symbol.Name}__");
        sb.AppendLine($"in [{symbolUi.Symbol.Namespace}](lib)");
        sb.AppendLine("");
        sb.AppendLine($"___");
        sb.AppendLine("");
        sb.AppendLine($"{symbolUi.Description}");
        sb.AppendLine("");
        sb.AppendLine($"___");
        sb.AppendLine("");
            


        if (symbolUi.Symbol.InputDefinitions.Count > 1)
        {
            sb.AppendLine($"## Input Parameters");
            sb.AppendLine($"|Name (Relevancy & Type)|Description|");
            sb.AppendLine($"|----|-----------|");
                
            foreach (var inputDef in symbolUi.Symbol.InputDefinitions)
            {
                var uiInput = symbolUi.InputUis[inputDef.Id];

                sb.Append("|");
                sb.Append($"__{inputDef.Name}__");
                sb.Append(" (");
                sb.Append(inputDef.DefaultValue.ValueType.Name);
                sb.Append(uiInput.Relevancy != Relevancy.Optional ? uiInput.Relevancy : string.Empty);
                sb.Append(")|");
                sb.Append(string.IsNullOrEmpty(uiInput.Description) ? "-" : uiInput.Description.Replace("\n", "<BR/>"));
                sb.AppendLine("|");
            }
        }
            
        {
            sb.AppendLine($"## Outputs");
            sb.AppendLine($"|Name|Type|");
            sb.AppendLine($"|----|---|");
            foreach (var outputDef in symbolUi.Symbol.OutputDefinitions)
            {
                var uiOutput = symbolUi.OutputUis[outputDef.Id];
                sb.AppendLine($"|__{outputDef.Name}__|{uiOutput.Type}|");
            }
        }

        sb.AppendLine("");
        sb.AppendLine("___");
        sb.AppendLine("");
        sb.AppendLine("⚠ Everything else is automatically generated and will be overwritten regularly.");
            
        File.WriteAllText(filepath, sb.ToString());
    }

    private const string WikiOperatorsFolder = "t3.wiki/operators/";
    private const string DescriptionMissing = "description missing";
}