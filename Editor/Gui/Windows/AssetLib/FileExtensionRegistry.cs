#nullable enable
using System.IO;

namespace T3.Editor.Gui.Windows.AssetLib;


/// <summary>
/// Helper class to convert extensions and file filters into unique ids for faster matching
/// </summary>
internal static class FileExtensionRegistry
{
    public static int GetUniqueId(string ext) => _map.TryGetValue(ext, out var id) 
                                               ? id 
                                               : _map[ext]=_next++;

    internal static bool TryGetExtensionIdForFilePath(string filepath, out int id)
    {
        id = -1;

        try
        {
            var extension = Path.GetExtension(filepath);
            if (extension.Length < 2)
                return false;

            if (!_map.TryGetValue(extension[1..], out id))
            {
                id = -1;
                return false;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Can't get extension from {filepath}: " + e.Message);
            return false;
        }
        
        return true;
    }
    
    public static List<int> IdsFromFileFilter(string filter)
    {
        var list = new List<int>();
        IdsFromFileFilter(filter, ref list);
        return list;
    }

    public static bool TryGetExtensionForId(int id, out string foundExtension)
    {
        foreach (var (e, i) in _map)
        {
            if (i != id) 
                continue;
            
            foundExtension = e;
            return true;
        }
        
        foundExtension = string.Empty;
        return false;
    }

    public static void IdsFromFileFilter(string filter, ref List<int> ids)
    {
        ids.Clear();
        
        var tokens = filter.Split('|');            // desc|pat|desc|pat|...

        for (int i = 1; i < tokens.Length; i += 2)
        {
            var patterns = tokens[i].Split(';');   // *.png;*.jpg;*.tar.gz
            foreach (var pat in patterns)
            {
                var ext = ExtractExtensionFromPattern(pat);
                if (ext is null) continue;
                ids.Add(GetUniqueId(ext));
            }
        }
    }

    // Returns normalized extension without leading dot, keeps composite (e.g. "tar.gz"), or null to skip.
    private static string? ExtractExtensionFromPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var s = pattern.Trim();

        // Skip all-files patterns
        if (s == "*" || s == "*.*") return null;

        // "*.ext" or "*.tar.gz" → "ext" / "tar.gz"
        if (s.Length >= 2 && s[0] == '*' && s[1] == '.')
            return s[2..].Trim().TrimStart('.').ToLowerInvariant();

        // Plain ".ext" or "ext" → "ext"
        if (!s.Contains('*'))
            return s.TrimStart('.').ToLowerInvariant();

        // Anything with wildcards beyond the leading "*." is unsupported → skip
        return null;
    }
    
    
    private static readonly Dictionary<string,int> _map = new(StringComparer.OrdinalIgnoreCase);
    private static int _next;
}