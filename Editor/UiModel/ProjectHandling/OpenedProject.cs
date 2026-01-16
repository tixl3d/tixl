#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Model;
using T3.Core.Operator;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed class OpenedProject
{
    public readonly EditorSymbolPackage Package;
    public readonly Structure Structure;

    public static readonly Dictionary<EditorSymbolPackage, OpenedProject> OpenedProjects = new();

    public static bool TryCreate(EditorSymbolPackage project,
                                 [NotNullWhen(true)] out OpenedProject? openedProject,
                                 [NotNullWhen(false)] out string? failureLog)
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

        openedProject
            = new OpenedProject(project,
                                () =>
                                {
                                    var symbol = project.Symbols[project.HomeSymbolId];
                                    if (symbol.TryGetParentlessInstance(out var instance))
                                        return instance.SymbolChild;

                                    Log.Error("Root instance could not be created?");
                                    return null!;
                                }
                               );

        OpenedProjects[openedProject.Package] = openedProject;
        return true;
    }

    public static bool TryCreateWithExplicitHome(EditorSymbolPackage project,
                                                 Guid implicitHomeOpId,
                                                 [NotNullWhen(true)] out OpenedProject? openedProject,
                                                 [NotNullWhen(false)] out string? failureLog)
    {
        failureLog = null;
        project.OverrideHomeGuid = implicitHomeOpId;
        
        // if (OpenedProjects.TryGetValue(project, out openedProject))
        // {
        //     failureLog = null;
        //     return true;
        // }

        openedProject
            = new OpenedProject(project,
                                () =>
                                {
                                    var homeSymbol = project.Symbols[implicitHomeOpId];
                                    if (homeSymbol.TryGetParentlessInstance(out var instance))
                                        return instance.SymbolChild;

                                    Log.Error("Root instance could not be created?");
                                    return null!;
                                }
                               );

        //OpenedProjects[openedProject.Package] = openedProject;
        return true;
    }

    private OpenedProject(EditorSymbolPackage project, Func<Symbol.Child> rootAction)
    {
        Package = project;
        Structure = new Structure(rootAction);
    }
}