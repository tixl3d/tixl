using System.Numerics;

namespace SilkWindows;

/// <param name="Position">Top-left corner in virtual-desktop pixels; null keeps the platform default.</param>
public readonly record struct SimpleWindowOptions(Vector2 Size, int Fps, bool Vsync, bool IsResizable, bool AlwaysOnTop = true, Vector2? Position = null);