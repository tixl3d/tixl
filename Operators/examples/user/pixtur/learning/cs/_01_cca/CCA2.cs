namespace Examples.user.pixtur.learning.cs._01_cca;

[Guid("2d0cd5e6-361a-43c3-8daa-3be2c301dbc4")]
public class CCA2 : Instance<CCA2>
{
    [Output(Guid = "185c9d4c-6d88-46c4-b41b-9cb87bbb71f3")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "c116d483-da8c-4ffa-9630-c687fc0ca205")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "8f7d797d-b35b-4cd5-8929-bef91eaf2967")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "f97f4836-7ec7-404c-9d67-ecfb9754117d")]
    public readonly InputSlot<float> MaxSteps = new();

    [Input(Guid = "fbd5b40b-eb15-443a-b397-2decd62263aa")]
    public readonly InputSlot<float> Range = new();

    [Input(Guid = "ce73442c-4058-48b0-9c0c-21254003b281")]
    public readonly InputSlot<float> RandomAmount = new();

    [Input(Guid = "a900c432-8336-4eee-8250-d0198e58dbea")]
    public readonly InputSlot<bool> AddNoise2 = new();

    [Input(Guid = "b7790535-f8bf-4e70-8d72-9eb55b5e9b2b")]
    public readonly InputSlot<bool> FullSpeed = new();

    [Input(Guid = "7e80cad4-2a66-480e-8f4f-5d037d8e98bc")]
    public readonly InputSlot<float> R_xThreshold = new();

    [Input(Guid = "12529c32-ead2-421c-a6d4-f638275bfaed")]
    public readonly InputSlot<float> G_xStates = new();

    [Input(Guid = "ae3e61d5-a6d0-445e-bcb8-26aa4d301ff5")]
    public readonly InputSlot<bool> UseMooreRegion = new();

    [Input(Guid = "4b34b3cf-967c-44d2-9b97-251de312f9cf")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "36516243-681f-4147-a14f-6d41a2cbbe42")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "2b067147-6b90-4e00-99a7-32e66d3075bb")]
    public readonly MultiInputSlot<Command> AdditionalGemeotry = new();

}