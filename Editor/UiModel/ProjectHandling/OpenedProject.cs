#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed class OpenedProject
{
    public readonly EditorSymbolPackage Package;
    public readonly Structure Structure;
    
    // TODO: This is not updated or used?
    private readonly List<ProjectView> _projectViews = [];

    public static readonly Dictionary<EditorSymbolPackage, OpenedProject> OpenedProjects = new();

    public static bool TryCreate(EditorSymbolPackage project, [NotNullWhen(true)] out OpenedProject? openedProject, [NotNullWhen(false)] out string? failureLog)
    {
        if (OpenedProjects.TryGetValue(project, out openedProject))
        {
            failureLog = null;
            return true;
        }
        
        if (!project.HasHomeSymbol(out failureLog))
        {
            failureLog ??= "project has no home?";
            openedProject = null;
            return false;
        }

        openedProject = new OpenedProject(project);
        OpenedProjects[openedProject.Package] = openedProject;
        return true;
    }

    private OpenedProject(EditorSymbolPackage project)
    {
        Package = project;
        Structure = new Structure(() =>
                                  {
                                      var symbol = project.Symbols[project.HomeSymbolId];
                                      if (!symbol.TryGetParentlessInstance(out var instance))
                                      {
                                          Log.Error("Root instance could not be created?");
                                          return null!;
                                      }

                                      return instance.SymbolChild!;
                                  });
    }
}