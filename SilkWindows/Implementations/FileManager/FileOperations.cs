namespace SilkWindows.Implementations.FileManager;

internal static class FileOperations
{
    public enum MoveType
    {
        Move,
        Copy
    }
    
    /// <summary>
    /// Returns true if a file operation was performed successfully, false if skipped or failed
    /// </summary>
    public static bool TryMoveDirectory(MoveType moveType, DirectoryInfo source, DirectoryInfo destination,
                                        Func<FileInfo, FileConflictOption> fileConflictHandler)
    {
        if (source.FullName == destination.FullName)
            return false;
        
        // dont move if the destination is a subdirectory of the source
        if (destination.FullName.StartsWith(source.FullName))
            return false;
        
        // create subfolder
        DirectoryInfo newDirectory;
        
        try
        {
            newDirectory = destination.CreateSubdirectory(source.Name);
        }
        catch(Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
        
        var operationPerformed = false;
        foreach (var file in source.EnumerateFiles())
        {
            operationPerformed |= TryMoveFile(moveType, file, newDirectory, fileConflictHandler);
        }
        
        foreach (var subDir in source.EnumerateDirectories())
        {
            operationPerformed |= TryMoveDirectory(moveType, subDir, newDirectory, fileConflictHandler);
        }
        
        try
        {
            if (source.GetFileSystemInfos().Length == 0 && moveType == MoveType.Move)
            {
                source.Delete();
                operationPerformed = true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        
        return operationPerformed;
    }
    
    /// <summary>
    /// Returns true if a file operation was performed successfully - false if a failure occurs or the file was skipped
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static bool TryMoveFile(MoveType moveType, FileInfo file, DirectoryInfo targetDirectory, Func<FileInfo, FileConflictOption> shouldOverwrite)
    {
        var newFileInfo = new FileInfo(Path.Combine(targetDirectory.FullName, file.Name));
        
        if (newFileInfo.FullName == file.FullName)
            return false;
        
        if (newFileInfo.Exists)
        {
            switch (shouldOverwrite(newFileInfo))
            {
                case FileConflictOption.Skip:
                    Console.WriteLine("File already exists, skipping.");
                    return false;
                case FileConflictOption.Overwrite:
                    break;
                case FileConflictOption.Rename:
                    throw new NotImplementedException(); // todo : create rename window, replace newFileInfo
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        try
        {
            // just in case
            targetDirectory.Create();
            
            switch (moveType)
            {
                case MoveType.Move:
                    file.MoveTo(newFileInfo.FullName, true);
                    break;
                case MoveType.Copy:
                    file.CopyTo(newFileInfo.FullName, true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(moveType), moveType, null);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
        
        return true;
    }
    
    public static IEnumerable<FileSystemInfo> PathsToFileSystemInfo(IEnumerable<string> paths)
    {
        return paths
              .Where(Path.Exists)
              .Select(path =>
                      {
                          if (Path.Exists(path))
                          {
                          }
                          
                          var attributes = File.GetAttributes(path);
                          if (attributes.HasFlag(FileAttributes.Directory))
                              return new DirectoryInfo(path);
                          
                          return (FileSystemInfo)new FileInfo(path);
                      });
    }
}

internal enum FileConflictOption
{
    Skip,
    Overwrite,
    Rename
}