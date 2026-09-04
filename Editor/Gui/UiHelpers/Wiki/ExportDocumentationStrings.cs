#nullable enable

using System.IO;
using T3.Editor.UiModel;
using T3.Serialization;

// ReSharper disable NotAccessedField.Local

namespace T3.Editor.Gui.UiHelpers.Wiki;

/// <summary>
/// Exports a json string for automatically review and correction. 
/// </summary>
internal static class ExportDocumentationStrings
{
    public static void ExportDocumentationAsJson()
    {
        var results = new List<DocumentationEntry>();
        

        foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
        {
            if (!string.IsNullOrWhiteSpace(symbolUi.Description))
            {
                results.Add(new DocumentationEntry
                                {
                                    Type = DocumentationEntry.Types.Description,
                                    Text = symbolUi.Description,
                                    SymbolId = symbolUi.Symbol.Id,
                                    Id = Guid.Empty
                                });
            }

            foreach (var param in symbolUi.InputUis.Values)
            {
                if (string.IsNullOrEmpty(param.Description))
                    continue;

                results.Add(new DocumentationEntry
                                {
                                    Type = DocumentationEntry.Types.ParameterDescription,
                                    Text = param.Description,
                                    SymbolId = symbolUi.Symbol.Id,
                                    Id = param.Id
                                });
            }

            foreach (var section in symbolUi.Sections.Values)
            {
                results.Add(new DocumentationEntry
                                {
                                    Type = DocumentationEntry.Types.Section,
                                    Text = section.Title,
                                    SymbolId = symbolUi.Symbol.Id,
                                    Id = section.Id
                                });
            }

            foreach (var childUi in symbolUi.ChildUis.Values)
            {
                if (string.IsNullOrWhiteSpace(childUi.Comment))
                    continue;

                results.Add(new DocumentationEntry
                                {
                                    Type = DocumentationEntry.Types.Comment,
                                    Text = childUi.Comment,
                                    SymbolId = symbolUi.Symbol.Id,
                                    Id = childUi.Id
                                });
            }
        }

        if (!Directory.Exists(DocumentationFolder))
            Directory.CreateDirectory(DocumentationFolder);
        
        const int pageSize = 50; // Define the size of each page
        for (var i = 0; i < results.Count; i += pageSize)
        {
            var currentPage = results.Skip(i).Take(pageSize).ToList();
            var pageIndex = i / pageSize;

            var filepath = Path.Combine(DocumentationFolder, $"{DocumentationBaseFilename}-{pageIndex:000}.{DocumentationFileExtension}");
            var fullPath = Path.GetFullPath(filepath);
            Log.Debug($"Writing {fullPath}...");
            if (File.Exists(filepath))
            {
                try
                {
                    File.Delete(filepath);
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to deleted {filepath} " + e.Message);
                }
            }
            JsonUtils.TrySaveJson(currentPage, filepath);
        }
    }

    public static void ImportDocumentationAsJson()
    {
        if (!Directory.Exists(DocumentationFolder))
        {
            Log.Warning($"{DocumentationFolder} not found");
            return;
        }

        foreach (var filename in Directory.GetFiles(DocumentationFolder))
        {
            var results = JsonUtils.TryLoadingJson<List<DocumentationEntry>>(filename);
            if (results == null)
            {
                Log.Warning($"Failed to load or parse {filename}");
                continue;
            }

            foreach (var r in results)
            {
                if (!SymbolUiRegistry.TryGetSymbolUi(r.SymbolId, out var symbolUi)) 
                    continue;
                
                switch (r.Type)
                {
                    case DocumentationEntry.Types.Description:
                        symbolUi.Description = r.Text;
                        break;

                    case DocumentationEntry.Types.ParameterDescription:
                        if (symbolUi.InputUis.TryGetValue(r.Id, out var inputUi))
                        {
                            inputUi.Description = r.Text;
                        }

                        break;

                    case DocumentationEntry.Types.Section:
                        if (symbolUi!.Sections.TryGetValue(r.Id, out var a))
                        {
                            a.Title = r.Text;
                        }

                        break;

                    case DocumentationEntry.Types.Comment:
                        if (symbolUi!.ChildUis.TryGetValue(r.Id, out var childUi))
                        {
                            childUi.Comment = r.Text;
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                symbolUi.FlagAsModified();
            }
        }
    }

    private sealed class DocumentationEntry
    {
        public enum Types
        {
            Description,
            ParameterDescription,
            Section,
            Comment,
        }

        public Types Type;
        public string Text = string.Empty;
        public Guid SymbolId;
        public Guid Id;
    }

    private const string DocumentationFolder = "docs";
    private const string DocumentationBaseFilename = "page";
    private const string DocumentationFileExtension = "json";
}