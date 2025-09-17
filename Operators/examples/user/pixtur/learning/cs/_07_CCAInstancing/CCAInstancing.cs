namespace Examples.user.pixtur.learning.cs._07_CCAInstancing;

[Guid("d8453963-b549-4ea9-aee5-6bb6bcf8e275")]
public class CCAInstancing : Instance<CCAInstancing>
{
    [Output(Guid = "1aa2207b-b526-4826-bd98-fdb382e8c492")]
    public readonly Slot<Texture2D> Output = new();


}