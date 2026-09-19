namespace Examples.user.changes;

[Guid("dbd8e542-730a-4417-badf-acc721a3eca8")]
internal sealed class ChangesScannerFx : Instance<ChangesScannerFx>
{
    [Output(Guid = "43eb6605-b042-4516-80bf-8326adb86c1e")]
    public readonly Slot<Texture2D> ImgOutput = new Slot<Texture2D>();


}