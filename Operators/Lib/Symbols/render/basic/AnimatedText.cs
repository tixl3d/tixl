using Lib.Utils;

namespace Lib.render.basic;

[Guid("2e4ecee7-2975-46e1-8f98-900935559176")]
internal sealed class AnimatedText : Instance<AnimatedText>
{
    [Output(Guid = "ad9aa97f-0089-4fe5-9aa6-bca5e0dd4886")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "72e3e29b-ecc4-4b5c-9814-2b4353a5ee60")]
    public readonly InputSlot<string> Text = new();

    [Input(Guid = "f0434b18-08fc-4429-ae39-b426bbdf9e39")]
    public readonly InputSlot<string> Font = new();

    [Input(Guid = "a0ccdc32-2948-4237-88ef-23858c6bcca5")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "c513f9cb-1cbe-493d-8fea-08c0b056b535")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "9eef0588-949a-477d-ab40-6e93020c47be")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "86cf8af8-2a75-4aa5-84bc-8e99cc89fab1", MappedType = typeof(BmFontDescription.HorizontalAligns))]
    public readonly InputSlot<int> HorizontalAlign = new();

    [Input(Guid = "385917c7-7925-4f4d-b085-28e8ef6e8323")]
    public readonly InputSlot<float> LineHeight = new();

    [Input(Guid = "35b2c723-c605-44e3-b9dd-4c853570241e")]
    public readonly InputSlot<string> GroupSeparator = new();

    [Input(Guid = "9993e5fc-f22b-4b58-905b-7a7c13ab1ea4", MappedType = typeof(Units))]
    public readonly InputSlot<int> Unit = new();

    [Input(Guid = "50721b83-b07f-41dc-bf5b-4b543bf02fe6")]
    public readonly InputSlot<float> RangeEnd = new();

    [Input(Guid = "ef449f5e-dc47-489e-ba17-84acd7800dff")]
    public readonly InputSlot<float> RangeStart = new();

    [Input(Guid = "7ae847bd-8d12-46c3-90f5-47666149996b")]
    public readonly InputSlot<float> Spread = new();

    [Input(Guid = "e8c60a31-8e55-4a47-bec1-c5998fe02a2a")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "031a5894-b13c-4cca-8c54-4722c3fd3274")]
    public readonly InputSlot<float> Randomize = new();

    [Input(Guid = "2e873d7e-1eff-4662-b776-e3253f7b5311")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "c1ce422e-00f4-4a18-ae04-c51c101357fb")]
    public readonly InputSlot<Vector3> PositionOffset = new();

    [Input(Guid = "85c71399-f382-4337-9a24-397864117702")]
    public readonly InputSlot<Vector3> RotationOffset = new();

    [Input(Guid = "ee6cdef1-705e-47c5-bbd5-9447bd263f33")]
    public readonly InputSlot<Vector2> Scale = new();

    [Input(Guid = "4b6b2893-f913-453b-89aa-7152358c55a0")]
    public readonly InputSlot<Vector2> Pivot = new();

    [Input(Guid = "604da803-e6e8-401e-a238-aa003158ac13")]
    public readonly InputSlot<float> OutDirection = new();

    [Input(Guid = "1806bf13-4e8a-441b-b6c9-d57a063c6a76")]
    public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new();

    private enum Units
    {
        Characters,
        Groups,
    }
}