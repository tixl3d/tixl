// ReSharper disable UnusedMember.Global
namespace T3.Core.Utils;

public class SharedEnums
{
    public enum BlendModes
    {
        Normal,
        Additive,
        Multiply,
        Invert,
        None,
        PreMultipliedExperimental,
        BlendOnWhite,
        BlendOnWhite01,
        UseImage_Alpha,
    }

    public enum RgbBlendModes
    {
        Normal = 0,
        Screen = 1,
        Multiply = 2,
        Overlay = 3,
        Difference = 4,
        UseImageA_RGB = 5,
        UseImageB_RGB = 6,
        ColorDodge = 7,
        LinearDodge = 8,
        MultiplyA = 9,

    }
}