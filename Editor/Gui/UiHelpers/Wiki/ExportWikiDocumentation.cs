using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using T3.Core.Operator;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.UiHelpers.Wiki;

/// <summary>
/// Exports per-operator documentation into <c>.help/docs/operators/</c>,
/// matching the nested URL shape used by the MkDocs site.
/// </summary>
public static class ExportWikiDocumentation
{
    public static void ExportWiki()
    {
        var exported = new List<SymbolUi>();

        foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
        {
            try
            {
                var symbol = symbolUi.Symbol;
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

                exported.Add(symbolUi);

                var absDir = Path.Combine(GetOperatorsRoot(), NamespaceToRelDir(symbol.Namespace));
                if (!Directory.Exists(absDir))
                    Directory.CreateDirectory(absDir);

                var filepath = Path.Combine(absDir, symbol.Name + ".md");
                WriteDocFile(filepath, symbolUi);
            }
            catch (Exception e)
            {
                Log.Error($" can't save docs for {symbolUi.Symbol.Name}: {e.Message}");
            }
        }

        try
        {
            WriteNamespaceIndices(exported);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to write namespace index pages: {e.Message}");
        }

        try
        {
            WriteLinkerIndexJson(exported);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to write operator index.json: {e.Message}");
        }

        Log.Debug($"Exported {exported.Count} operator pages into {GetOperatorsRoot()}");
    }

    /// <summary>
    /// Resolves <see cref="HelpOperatorsFolder"/> against the current working directory. In DEBUG
    /// builds the editor runs from <c>bin/Debug/netX.X-windows/</c>, so prepend <c>../../../..</c>
    /// to land back in the repo root. Release builds (run from an install dir) stay relative.
    /// </summary>
    private static string GetOperatorsRoot()
    {
#if DEBUG
        return Path.Combine("../../../..", HelpOperatorsFolder);
#else
        return HelpOperatorsFolder;
#endif
    }

    /// <summary>
    /// <c>Lib.field.adjust</c> → <c>lib/field/adjust</c>. Lowercased so the URL is stable
    /// independent of the namespace's casing preferences.
    /// </summary>
    private static string NamespaceToRelDir(string ns)
    {
        return ns.ToLowerInvariant().Replace('.', Path.DirectorySeparatorChar);
    }

    private static string NamespaceToUrlPath(string ns)
    {
        return ns.ToLowerInvariant().Replace('.', '/');
    }

    private static string GetOperatorUrl(Symbol symbol)
    {
        return "/operators/" + NamespaceToUrlPath(symbol.Namespace) + "/" + symbol.Name + "/";
    }

    private static string GetSummary(SymbolUi symbolUi)
    {
        var desc = symbolUi.Description;
        if (string.IsNullOrWhiteSpace(desc) || desc == DescriptionMissing)
            return string.Empty;
        return desc.Split('\n')[0].Trim();
    }

    private static void WriteDocFile(string filepath, SymbolUi symbolUi)
    {
        var symbol = symbolUi.Symbol;
        var sb = new StringBuilder();

        sb.AppendLine($"# {symbol.Name}");
        sb.AppendLine();
        sb.AppendLine($"*in [{symbol.Namespace}](README.md)*");
        sb.AppendLine();

        var desc = string.IsNullOrWhiteSpace(symbolUi.Description) ? DescriptionMissing : symbolUi.Description;
        sb.AppendLine(desc);
        sb.AppendLine();

        if (symbol.InputDefinitions.Count > 1)
        {
            sb.AppendLine("## Input Parameters");
            sb.AppendLine("| Name (Relevancy & Type) | Description |");
            sb.AppendLine("|---|---|");

            foreach (var inputDef in symbol.InputDefinitions)
            {
                var uiInput = symbolUi.InputUis[inputDef.Id];
                var typeName = inputDef.DefaultValue.ValueType.Name;
                var relevancy = uiInput.Relevancy != Relevancy.Optional ? uiInput.Relevancy.ToString() : string.Empty;
                var relevancyTag = string.IsNullOrEmpty(relevancy) ? string.Empty : " " + relevancy;
                var descCell = string.IsNullOrEmpty(uiInput.Description) ? "—" : uiInput.Description.Replace("\n", "<br/>");
                sb.AppendLine($"| **{inputDef.Name}** ({typeName}{relevancyTag}) | {descCell} |");
            }
            sb.AppendLine();
        }

        if (symbol.OutputDefinitions.Count > 0)
        {
            sb.AppendLine("## Outputs");
            sb.AppendLine("| Name | Type |");
            sb.AppendLine("|---|---|");
            foreach (var outputDef in symbol.OutputDefinitions)
            {
                var uiOutput = symbolUi.OutputUis[outputDef.Id];
                sb.AppendLine($"| **{outputDef.Name}** | {uiOutput.Type} |");
            }
            sb.AppendLine();
        }
        
        File.WriteAllText(filepath, sb.ToString());
    }

    /// <summary>
    /// Writes a <c>README.md</c> at each namespace level, listing the child namespaces and
    /// operators at that level. Replaces the old monolithic <c>lib.md</c> TOC.
    /// </summary>
    private static void WriteNamespaceIndices(List<SymbolUi> exported)
    {
        var opsByNamespace = new Dictionary<string, List<SymbolUi>>();
        var childrenByNamespace = new Dictionary<string, SortedSet<string>>();

        foreach (var symbolUi in exported)
        {
            var ns = symbolUi.Symbol.Namespace;
            if (!opsByNamespace.TryGetValue(ns, out var list))
            {
                list = new List<SymbolUi>();
                opsByNamespace[ns] = list;
            }
            list.Add(symbolUi);

            // Register this namespace with each of its ancestors as a child.
            var parts = ns.Split('.');
            for (var i = 1; i < parts.Length; i++)
            {
                var parentNs = string.Join('.', parts.Take(i));
                var childNs = string.Join('.', parts.Take(i + 1));
                if (!childrenByNamespace.TryGetValue(parentNs, out var children))
                {
                    children = new SortedSet<string>(StringComparer.Ordinal);
                    childrenByNamespace[parentNs] = children;
                }
                children.Add(childNs);
            }
        }

        var allNamespaces = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var ns in opsByNamespace.Keys) allNamespaces.Add(ns);
        foreach (var ns in childrenByNamespace.Keys) allNamespaces.Add(ns);

        foreach (var ns in allNamespaces)
        {
            var relDir = NamespaceToRelDir(ns);
            var dir = Path.Combine(GetOperatorsRoot(), relDir);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine($"# {ns}");
            sb.AppendLine();

            if (childrenByNamespace.TryGetValue(ns, out var children) && children.Count > 0)
            {
                sb.AppendLine("## Sub-namespaces");
                sb.AppendLine();
                foreach (var childNs in children)
                {
                    var leaf = childNs.Substring(childNs.LastIndexOf('.') + 1);
                    sb.AppendLine($"- [{childNs}]({leaf.ToLowerInvariant()}/README.md)");
                }
                sb.AppendLine();
            }

            if (opsByNamespace.TryGetValue(ns, out var ops) && ops.Count > 0)
            {
                sb.AppendLine("## Operators");
                sb.AppendLine();
                foreach (var op in ops.OrderBy(o => o.Symbol.Name, StringComparer.Ordinal))
                {
                    var summary = GetSummary(op);
                    var suffix = string.IsNullOrEmpty(summary) ? string.Empty : " — " + summary;
                    sb.AppendLine($"- [**{op.Symbol.Name}**]({op.Symbol.Name}.md){suffix}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("*Auto-generated from the operator library.*");

            File.WriteAllText(Path.Combine(dir, "README.md"), sb.ToString());
        }
    }

    /// <summary>
    /// Writes a JSON sidecar consumed by the MkDocs auto-linker
    /// (<c>.help/scripts/docs/op_autolinks.py</c>) to resolve <c>[OperatorName]</c> references.
    /// </summary>
    private static void WriteLinkerIndexJson(List<SymbolUi> exported)
    {
        var byFullPath = new SortedDictionary<string, IndexEntry>(StringComparer.Ordinal);
        var byShortName = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var symbolUi in exported)
        {
            var symbol = symbolUi.Symbol;
            var fullPath = symbol.Namespace + "." + symbol.Name;

            byFullPath[fullPath] = new IndexEntry
                                       {
                                           Url = GetOperatorUrl(symbol),
                                           Summary = GetSummary(symbolUi),
                                       };

            if (!byShortName.TryGetValue(symbol.Name, out var list))
            {
                list = new List<string>();
                byShortName[symbol.Name] = list;
            }
            list.Add(fullPath);
        }

        foreach (var list in byShortName.Values)
            list.Sort(StringComparer.Ordinal);

        var root = new IndexRoot
                       {
                           ByFullpath = byFullPath,
                           ByShortname = byShortName,
                       };

        var options = new JsonSerializerOptions
                          {
                              WriteIndented = true,
                              PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                              Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                          };

        var json = JsonSerializer.Serialize(root, options);
        File.WriteAllText(Path.Combine(GetOperatorsRoot(), "index.json"), json);
    }

    private sealed class IndexRoot
    {
        public SortedDictionary<string, IndexEntry> ByFullpath { get; set; } = new();
        public SortedDictionary<string, List<string>> ByShortname { get; set; } = new();
    }

    private sealed class IndexEntry
    {
        public string Url { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    private const string HelpOperatorsFolder = ".help/docs/operators";
    private const string DescriptionMissing = "*No description yet. Edit this operator's description in the TiXL editor to populate this page.*";
}
