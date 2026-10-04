namespace T3.Core.Output;

/// <summary>How a surface's quad relates to the output canvas it is shown on.</summary>
public enum MappingModes
{
    /// <summary>Four corners the user placed: aimed at a wall, and re-projected along when the surface's
    /// rectangle is reshaped on the wall.</summary>
    CornerPin,

    /// <summary>The whole canvas, derived: the output *is* the surface, so nothing about the surface's metres
    /// or its place in the room moves it.</summary>
    Fill,
}
