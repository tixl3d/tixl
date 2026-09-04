using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Animation;

public sealed class DeleteKeyframeCommand : ICommand
{
    public string Name => "Delete keyframe";
    public bool IsUndoable => true;
        
    public DeleteKeyframeCommand(Curve curve, VDefinition keyframe, bool wasSelected = false)
    {
        _curve = curve;
        _keyframe = keyframe;
        _wasSelected = wasSelected;

    }
    public void Do()
    {
        _curve.RemoveKeyframeAt(_keyframe.U);
        _curve.UpdateTangents();
    }
        
    public void Undo()
    {
        _curve.AddOrUpdateV(_keyframe.U, _keyframe);                
        _curve.UpdateTangents();
        if (_wasSelected)
        {
            ProjectView.Focused?.TimeLineCanvas.DopeSheetArea.TrySelectKeyFrame(_curve, _keyframe);
        }
        //ProjectView.Focused?.TimeLineCanvas.DopeSheetArea.SelectedKeyframes.Add(_keyframe);
    }

    private readonly Curve _curve;
    private readonly VDefinition _keyframe;
    private readonly bool _wasSelected;
}