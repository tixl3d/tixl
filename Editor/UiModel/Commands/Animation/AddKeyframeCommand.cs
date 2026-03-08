#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes;

namespace T3.Editor.UiModel.Commands.Animation;

public sealed class AddKeyframesCommand : ICommand
{
    public string Name => "Add keyframe";
    public bool IsUndoable => true;
        
    private readonly Curve _curve;
    private readonly VDefinition? _originalKey;
    private readonly VDefinition _newKey;
        

    public AddKeyframesCommand(Curve curve, VDefinition newKey)
    {
        _curve = curve;
        _originalKey = curve.GetV(newKey.U);
        _newKey = newKey;
    }
        
    public void Undo()
    {
        if (_originalKey != null)
        {
            _curve.AddOrUpdateV(_originalKey.U, _originalKey);                
        }
        else
        {
            _curve.RemoveKeyframeAt(_newKey.U);
        }
        _curve.UpdateTangents();
    }

    public void Do()
    {
        _curve.AddOrUpdateV(_newKey.U, _newKey);
        _curve.UpdateTangents();
    }
}