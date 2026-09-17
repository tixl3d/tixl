#nullable enable
using System.IO;
using Newtonsoft.Json;
using T3.Core.Model;
using T3.Core.Operator;

namespace T3.Editor.Migrations.SymbolFiles;

/// <summary>
/// Symbol files saved before TiXL 4.4 don't record the SymbolName of their children. The only hint at what
/// a missing operator was is the name comment the writer puts behind each child id. Symbol files are
/// parsed without comments, so this scans the raw file again — only for symbols with unresolved
/// children that lack a SymbolName. Can be removed once projects saved before 4.4 are rare.
/// </summary>
internal static class PreV4_4ChildNameComments
{
    internal static void ReadFor(Symbol symbol, string symbolFilePath)
    {
        if (!NeedsNames(symbol))
            return;

        try
        {
            using var streamReader = new StreamReader(symbolFilePath);
            using var jsonReader = new JsonTextReader(streamReader);

            Symbol.UnresolvedChild? childOfLastId = null;
            while (jsonReader.Read())
            {
                if (jsonReader.TokenType == JsonToken.Comment)
                {
                    if (childOfLastId != null && jsonReader.Value is string comment && !string.IsNullOrWhiteSpace(comment))
                        childOfLastId.FallbackName = comment.Trim();

                    childOfLastId = null;
                    continue;
                }

                childOfLastId = null;
                if (jsonReader.TokenType != JsonToken.String || !jsonReader.Path.EndsWith("." + SymbolJson.JsonKeys.Id))
                    continue;

                if (!Guid.TryParse(jsonReader.Value as string, out var id))
                    continue;

                foreach (var child in symbol.UnresolvedChildren)
                {
                    if (child.Id != id || child.SymbolName != null)
                        continue;

                    childOfLastId = child;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Can't read operator names from '{symbolFilePath}': {e.Message}");
        }
    }

    private static bool NeedsNames(Symbol symbol)
    {
        foreach (var child in symbol.UnresolvedChildren)
        {
            if (child.SymbolName == null)
                return true;
        }

        return false;
    }
}
