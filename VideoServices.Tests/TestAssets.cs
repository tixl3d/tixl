namespace T3.VideoServices.Tests;

/// <summary>Locates the checked-in sample videos by walking up to the repo root (the folder with t3.sln).</summary>
internal static class TestAssets
{
    public static string Video720p => Resolve("test-720p.mp4");
    public static string Video1080p => Resolve("spray-1080p.mp4");

    private static string Resolve(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "t3.sln")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate repo root (t3.sln) from " + AppContext.BaseDirectory);

        return Path.Combine(dir, "Operators", "examples", "Assets", "videos", fileName);
    }
}
