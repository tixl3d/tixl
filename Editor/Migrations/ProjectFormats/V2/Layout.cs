#nullable enable

namespace T3.Editor.Migrations.ProjectFormats.V2;

/// <summary>
/// Format V2: operator files live in <c>Symbols/</c>, project content is the allowlist in
/// <see cref="UiModel.ProjectLayout"/>. While V2 is the current format this class forwards to that
/// steady-state definition; when V3 ships, freeze the values here as literals and point
/// <see cref="UiModel.ProjectLayout"/> at the V3 definition instead.
/// </summary>
internal static class Layout
{
    public static string[] ContentSubdirectories => UiModel.ProjectLayout.ContentSubdirectories;
}
