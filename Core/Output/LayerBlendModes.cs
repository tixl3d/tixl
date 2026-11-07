namespace T3.Core.Output;

/// <summary>How a layer on an output's canvas combines with the layers under it.</summary>
public enum LayerBlendModes
{
    /// <summary>Covers what is under it, by its alpha.</summary>
    Normal,

    /// <summary>Adds its light: black leaves the layers under it unchanged.</summary>
    Add,

    /// <summary>Darkens: white leaves the layers under it unchanged, black blacks them out — a mask.</summary>
    Multiply,

    /// <summary>Brightens without clipping as hard as Add: black leaves the layers under it unchanged.</summary>
    Screen,
}
