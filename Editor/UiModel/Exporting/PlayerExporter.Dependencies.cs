#nullable enable
using System.IO;
using System.Reflection;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;

namespace T3.Editor.UiModel.Exporting;

internal static partial class PlayerExporter
{
    /// <summary>
    /// Decides which optional dependency files stay out of the export. A file named by an
    /// <see cref="ExportDependenciesAttribute"/> on any loaded operator is only shipped when one of the
    /// exported operators declares it; files no operator declares are always shipped.
    /// </summary>
    private sealed class DependencyFileFilter
    {
        public DependencyFileFilter(ExportData exportData)
        {
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in exportData.Symbols)
            {
                foreach (var pattern in GetDeclaredFiles(symbol))
                    required.Add(pattern);
            }

            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in EditorSymbolPackage.AllSymbols)
            {
                foreach (var pattern in GetDeclaredFiles(symbol))
                    all.Add(pattern);
            }

            all.ExceptWith(required);
            _excludedPatterns = all.ToList();
            _excludedPatterns.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> ExcludedPatterns => _excludedPatterns;

        public bool ShouldExcludeFile(string relativePath)
        {
            if (_excludedPatterns.Count == 0)
                return false;

            var fileName = Path.GetFileName(relativePath);
            foreach (var pattern in _excludedPatterns)
            {
                if (MatchesPattern(fileName, pattern))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> GetDeclaredFiles(Symbol symbol)
        {
            var type = symbol.InstanceType;
            if (type == null)
                return [];

            try
            {
                return type.GetCustomAttribute<ExportDependenciesAttribute>()?.FileNames ?? [];
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to read export dependencies of [{symbol.Name}]: {e.Message}");
                return [];
            }
        }

        /// <summary>Case-insensitive match where '*' stands for any sequence of characters.</summary>
        private static bool MatchesPattern(string fileName, string pattern)
        {
            var starIndex = pattern.IndexOf('*');
            if (starIndex < 0)
                return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);

            var parts = pattern.Split('*');
            var position = 0;
            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                var part = parts[partIndex];
                if (part.Length == 0)
                    continue;

                var found = fileName.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    return false;

                // The first part must anchor at the start, the last at the end
                if (partIndex == 0 && found != 0)
                    return false;

                position = found + part.Length;
            }

            var lastPart = parts[^1];
            return lastPart.Length == 0 || fileName.EndsWith(lastPart, StringComparison.OrdinalIgnoreCase);
        }

        private readonly List<string> _excludedPatterns;
    }
}
