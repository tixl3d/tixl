namespace Examples.Lib.volume;

/// <summary>
/// Three ways into a volume: from a mesh, from points and from a field.
/// </summary>
[Guid("afe8d595-838b-5aa2-8411-57bba4355642")]
internal sealed class VolumeFromMeshExample : Instance<VolumeFromMeshExample>
{
    [Output(Guid = "d60c0b83-c747-52e9-849b-05ece40eaf4b")]
    public readonly Slot<Texture2D> Output = new();
}
