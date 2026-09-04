namespace ProjectUpdater;

internal sealed partial class Conversion
{
    private FileChangeInfo ConvertAndMoveT3File(string filepath, string fileContents, string originalRootDirectory, string newRootDirectory)
    {
        const string guidKey = "\"Id\": \"";
        var startGuidKeyIndex = fileContents.IndexOf(guidKey, StringComparison.Ordinal);

        if (startGuidKeyIndex == -1)
        {
            Console.WriteLine($"Could not find Id in {filepath}");
            return new FileChangeInfo(filepath, fileContents);
        }

        var startGuidIndex = startGuidKeyIndex + guidKey.Length;
        var guidSpan = fileContents.AsSpan(startGuidIndex, 36);
        var gotGuid = Guid.TryParse(guidSpan, out var guid);

        if (!gotGuid)
        {
            Console.WriteLine($"Could not parse guid in \"{filepath}\": \"{guidSpan}\"");
            return FileChangeInfo.Empty;
        }

        if (!_destinationDirectories.TryGetValue(guid, out var destinationDirectory))
        {
            // deduce destination directory from json's "Namespace" field
            const string namespaceKey = "\"Namespace\": \"";
            var namespaceIndex = fileContents.IndexOf(namespaceKey, StringComparison.Ordinal);
            if (namespaceIndex == -1)
            {
                Console.WriteLine($"Could not find Namespace in {filepath}");
                return FileChangeInfo.Empty;
            }

            var namespaceStartIndex = namespaceIndex + namespaceKey.Length;
            var namespaceEndIndex = fileContents.IndexOf('\"', namespaceStartIndex);
            var namespaceSpan = fileContents.AsSpan(namespaceStartIndex, namespaceEndIndex - namespaceStartIndex);
            var namespaceSubDirectories = namespaceSpan.ToString().Split('.');
            var subDirs = Path.Combine(namespaceSubDirectories);
            var newDirectory = Path.Combine(newRootDirectory, subDirs);
            var newPath = Path.Combine(newDirectory, Path.GetFileName(filepath));

            _destinationDirectories[guid] = newDirectory;
            return new FileChangeInfo(newPath, fileContents);
        }

        var newFilePath = Path.Combine(destinationDirectory, Path.GetFileName(filepath));

        return new FileChangeInfo(newFilePath, fileContents);
    }
}