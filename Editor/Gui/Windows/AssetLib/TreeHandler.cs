#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Sadly there is no build in imgui method for closing all opened folders in a tree
/// (similar to shift clicking in explorer). Out work around is to first all open
/// folders and the close them frame by frame last to first. 
/// </summary>
internal sealed class TreeHandler
{
    internal bool NoFolderOpen; // useful to disable collapse icon
    
    internal void CollapseAll()
    {
        _state = TreeHandler.States.TriggeredAndCollecting;
        _closedAFolderWhileCollapsing = false;
        _openFolderHashCodes.Clear();
    }

    internal void Update()
    {
        NoFolderOpen = _noFoldersOpenNow;
        _noFoldersOpenNow = true;
        switch (_state)
        {
            case TreeHandler.States.TriggeredAndCollecting:
                _state = TreeHandler.States.Collapsing;
                break;
            
            case TreeHandler.States.Collapsing:
            {
                if (!_closedAFolderWhileCollapsing)
                {
                    _state = TreeHandler.States.NotActive;
                }

                _closedAFolderWhileCollapsing = false;
                break;
            }
        }
    }

    internal void UpdateForNode(int hashCode)
    {
        switch (_state)
        {
            case TreeHandler.States.TriggeredAndCollecting:
                _openFolderHashCodes.Push(hashCode);
                break;
                
            case TreeHandler.States.Collapsing:
            {
                if (_openFolderHashCodes.Count <= 0 || _openFolderHashCodes.Peek() != hashCode) 
                    return;
                
                _openFolderHashCodes.Pop();
                _closedAFolderWhileCollapsing = true;
                ImGui.SetNextItemOpen(false, ImGuiCond.Always);
                break;
            }
        }
    }

    internal void FlagLastItemWasVisible()
    {
        _noFoldersOpenNow = false;
    }

    internal void Reset()
    {
        _state = States.NotActive;
        _openFolderHashCodes.Clear();
    }
    
    private enum States
    {
        NotActive,
        TriggeredAndCollecting,
        Collapsing,
    }
    
    private readonly Stack<int> _openFolderHashCodes = []; // ordered, deepest last
    private States _state = States.NotActive;
    private bool _closedAFolderWhileCollapsing;
    private bool _noFoldersOpenNow;
}