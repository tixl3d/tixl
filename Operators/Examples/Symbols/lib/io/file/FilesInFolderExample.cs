namespace Examples.Lib.io.file;

[Guid("897a5f40-7970-4770-bd51-08a085f8355b")]
 internal sealed class FilesInFolderExample : Instance<FilesInFolderExample>
{
    [Output(Guid = "5ae5161d-010d-48ab-b0d0-8fc6a1d6e7ce")]
    public readonly Slot<Texture2D> Texture = new();


}