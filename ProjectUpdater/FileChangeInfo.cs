namespace ProjectUpdater;

internal readonly struct FileChangeInfo(string newFilePath, string newFileContents)
{
    private bool Equals(FileChangeInfo other)
    {
        return NewFilePath == other.NewFilePath && NewFileContents == other.NewFileContents;
    }
 
    public override bool Equals(object? obj)
    {
        return obj is FileChangeInfo other && Equals(other);
    }
 
    public override int GetHashCode()
    {
        return HashCode.Combine(NewFilePath, NewFileContents);
    }
 
    public readonly string NewFilePath = newFilePath;
    public readonly string NewFileContents = newFileContents;
         
    public static readonly FileChangeInfo Empty = new(string.Empty, string.Empty);
         
    // equality operators
    public static bool operator ==(FileChangeInfo left, FileChangeInfo right) => left.NewFilePath == right.NewFilePath 
                                                                                 && left.NewFileContents == right.NewFileContents;
         
    public static bool operator !=(FileChangeInfo left, FileChangeInfo right) => !(left == right);
}