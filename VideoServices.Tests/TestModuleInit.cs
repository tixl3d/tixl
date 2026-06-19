using System.Runtime.CompilerServices;

namespace T3.VideoServices.Tests;

internal static class TestModuleInit
{
    // The CI/dev machine may only have a GPL FFmpeg build installed. The license gate is a distribution
    // concern, not a decode concern, so allow it for tests (runs once before any test).
    [ModuleInitializer]
    internal static void Init() => FfmpegLibrary.AllowRestrictedBuildForTesting = true;
}
