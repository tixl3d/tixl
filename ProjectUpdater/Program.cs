namespace ProjectUpdater;

public class ProjectUpdater
{
    private static void Convert(string currentProjectRootDir, string newProjectRootDir)
    {
        var converter = new Conversion();
        converter.StartConversion(currentProjectRootDir, newProjectRootDir);
    }
}