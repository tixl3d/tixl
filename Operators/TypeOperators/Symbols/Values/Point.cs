using SharpDX;
using T3.Core.Utils;

namespace Types.Values;

[Guid("9989f539-f86c-4508-83d7-3fc0e559f502")]
public sealed class Point : Instance<Point>, ITransformable
{
    [Output(Guid = "5915D7E2-054D-4917-86BD-25AD1BEB1754")]
    public readonly Slot<BufferWithViews> Buffer = new();
        
    [Output(Guid = "D9C04756-8922-496D-8380-120F280EF65B")]
    public readonly Slot<StructuredList> ResultList = new();
        
    [Output(Guid = "8698D60D-8CD9-4A3F-9001-19DAC29028CC")]
    public readonly Slot<System.Numerics.Vector3> OutPosition = new();
        
        
    public Point()
    {
        UpdateBuffer(); // Force update when application starts (needed for executable export)
        Buffer.UpdateAction += UpdateWithBuffer;
        ResultList.UpdateAction += Update;
        OutPosition.UpdateAction += Update;
        _pointListWithSeparator.TypedElements[1] = T3.Core.DataTypes.Point.Separator();

    }
        
    IInputSlot ITransformable.TranslationInput => Position;
    IInputSlot? ITransformable.RotationInput => null;
    IInputSlot? ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext>? TransformCallback { get; set; }

    private void UpdateWithBuffer(EvaluationContext context)
    {
        Update(context);
        UpdateBuffer();
        Buffer.Value = _bufferWithViews;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context);
            
        var pos = Position.GetValue(context);
        _addSeparator = AddSeparator.GetValue(context);

        // If separator state changed, we need to rebuild the buffer
        var separatorChanged = _addSeparator != _lastAddSeparator;
        _lastAddSeparator = _addSeparator;

        var rot = Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.Normalize( RotationAxis.GetValue(context)), RotationAngle.GetValue(context) * MathUtils.ToRad);
        var array = _addSeparator ? _pointListWithSeparator : _pointList;
        OutPosition.Value = pos;
        array.TypedElements[0].Position = pos;
        array.TypedElements[0].F1 = F1.GetValue(context);
        array.TypedElements[0].Color = Color.GetValue(context);
        array.TypedElements[0].Scale = Size.GetValue(context) * Scale.GetValue(context);
        array.TypedElements[0].F2 = F2.GetValue(context);
        array.TypedElements[0].Orientation = rot;
        ResultList.Value = array;

        // Force buffer update if separator state changed or on first run
        if (separatorChanged || !_isBufferInitialized)
        {
            UpdateBuffer();
            _isBufferInitialized = true;
        }

        ResultList.DirtyFlag.Clear();
        OutPosition.DirtyFlag.Clear();
    }

    private void UpdateBuffer()
    {
        var source = _addSeparator ? _pointListWithSeparator : _pointList;
        var sizeChanged = _buffer == null || _buffer.Description.SizeInBytes != source.TotalSizeInBytes;
            
        using (var data = new DataStream(source.TotalSizeInBytes, true, true))
        {
            data.Position = 0;
            source.WriteToStream(data);

            try
            {
                ResourceManager.SetupStructuredBuffer(data, source.TotalSizeInBytes, T3.Core.DataTypes.Point.Stride, ref _buffer);
            }
            catch (Exception e)
            {
                Log.Error("Failed to setup structured buffer " + e.Message, this);
                return;
            }
        }

        if (sizeChanged)
        {
            // Log.Debug("Updating Point buffer size....", this);
            ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);
            _bufferWithViews.Buffer = _buffer;
        }
    }

    private readonly StructuredList<T3.Core.DataTypes.Point> _pointListWithSeparator = new(2);
    private readonly StructuredList<T3.Core.DataTypes.Point> _pointList = new(1);
        
    private Buffer? _buffer;
    private readonly BufferWithViews _bufferWithViews = new() ;
    private bool _addSeparator;
    private bool _lastAddSeparator;
    private bool _isBufferInitialized = false;

    [Input(Guid = "a0a453db-d8f1-415a-9a98-3c88a25b15e7")]
    public readonly InputSlot<System.Numerics.Vector3> Position = new();

    [Input(Guid = "130B5C11-66DD-4C0E-AC67-924554BAD2D8")]
    public readonly InputSlot<System.Numerics.Vector3> Size = new();

    [Input(Guid = "3CDC9CAB-36D4-431F-B9BD-C8D8F49EE465")]
    public readonly InputSlot<float> Scale = new();
    
    [Input(Guid = "2d7d85ce-7b5e-4e86-bae2-88a7c4f7a2e5")]
    public readonly InputSlot<float> F1 = new();
        
    [Input(Guid = "CA12DF13-7529-4EDE-B6FC-CE8AEBA4F33E")]
    public readonly InputSlot<float> F2 = new();
    
    [Input(Guid = "34AD759E-9A81-4D7E-9024-5ABACC279895")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();
        
    [Input(Guid = "55a3370f-1768-414f-b38d-4accc5e93914")]
    public readonly InputSlot<System.Numerics.Vector3> RotationAxis = new();

    [Input(Guid = "E9859381-EB88-4856-91C5-60D30AC6035A")]
    public readonly InputSlot<float> RotationAngle = new();
    
    [Input(Guid = "53CDE701-435F-42E4-B598-DB0E607A238C")]
    public readonly InputSlot<bool> AddSeparator = new();
        
}