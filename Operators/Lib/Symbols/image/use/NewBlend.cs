namespace Lib.image.use;

[Guid("4bf07ce8-b92b-4191-b14b-8c3862df9c01")]
internal sealed class NewBlend : Instance<NewBlend> {
  [Output(Guid = "8871c07c-ff14-4254-a7b8-2440a3f05ccf")]
  public readonly Slot<Texture2D> Output = new();

  [Input(Guid = "6049030b-6e3c-4e16-8166-5fab3418fac1")]
  public readonly InputSlot<float> Factor = new InputSlot<float>();

  [Input(Guid = "84312dab-efa1-413f-a8c5-8ddaf4ca5161")]
  public readonly InputSlot<Texture2D> ImageA = new InputSlot<Texture2D>();

  [Input(Guid = "18824d11-859c-4a2e-9853-2bea1fc94ff9")]
  public readonly InputSlot<Vector4> ColorA = new InputSlot<Vector4>();

  [Input(Guid = "b2987d6e-e11c-4d7a-abe5-1596d6fd588f")]
  public readonly InputSlot<Texture2D> ImageB = new InputSlot<Texture2D>();

  [Input(Guid = "9ad9834f-984a-441f-ae45-ababb164fd0a")]
  public readonly InputSlot<Vector4> ColorB = new InputSlot<Vector4>();

  [Input(Guid = "90b60dd5-ac4b-4291-8b93-853b74c81ac4",
         MappedType = typeof(RgbBlendModes))]
  public readonly InputSlot<int> BlendMode = new InputSlot<int>();

  [Input(Guid = "a6d4e532-706e-4ff5-a9b1-5a518d75a045",
         MappedType = typeof(AlphaBlendModes))]
  public readonly InputSlot<int> AlphaMode = new InputSlot<int>();

  [Input(Guid = "889ff315-8794-4287-b409-f39e7dacbd4e")]
  public readonly InputSlot<bool> ClampFactor = new InputSlot<bool>();

  [Input(Guid = "f711ae92-4697-409e-9de0-3d0c51973466")]
  public readonly InputSlot<bool> ClampOutput = new InputSlot<bool>();

  [Input(Guid = "66e6556e-138b-4e7c-9df1-55395fee3ca7")]
  public readonly InputSlot<T3.Core.DataTypes.Texture2D> ImageMask =
      new InputSlot<T3.Core.DataTypes.Texture2D>();

  [Input(Guid = "dbc5fc3c-dd29-4b3d-9669-156a519ebd3c")]
  public readonly InputSlot<bool> NormalForUpperHalf = new InputSlot<bool>();

  [Input(Guid = "f70cdc3e-477a-4e00-81b8-9520fd51432a",
         MappedType = typeof(ScaleModes))]
  public readonly InputSlot<int> ScaleMode = new InputSlot<int>();

  [Input(Guid = "5dfaade0-c157-4d8a-8bc5-6dfbae040ee0")]
  public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();

  [Input(Guid = "2bb75121-2bec-483a-8fc1-af7f25f29585")]
  public readonly InputSlot<Int2> Resolution = new InputSlot<Int2>();

  /* TODOO
   * Exclusion ??
   */
  private enum RgbBlendModes {
    Normal = 0,
    Add = 1,
    Sub = 2,
    Difference = 3,
    Multiply = 4,
    Div = 5,
    Screen = 6,
    Overlay = 7,
    ColorDodge = 8,
    Colorburn = 9,
    MixUsingImageB_A = 10,
    LinearLight = 11,
    SoflLight = 12,
    Darken = 13,
    Lighten = 14,
    Hue = 15,
    Saturation = 16,
    Color = 17,
    Luminosity = 18,
  }

  private enum AlphaBlendModes {
    Normal = 0,
    Multiply = 1,
    SetToOne = 2,
    Output_Alpha = 3,
    UseImageA_Alpha = 4,
    UseImageB_Alpha = 5,
    Output_Brightness = 6,
    UseImageA_Brightness = 7,
    UseImageB_Brightness = 8,
    Additive = 9,
    Max = 10,
  }

  private enum ScaleModes {
    Stretch = 0,
    Fit = 1,
    Cover = 2,
  }
}