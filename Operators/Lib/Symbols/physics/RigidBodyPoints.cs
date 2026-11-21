namespace Lib.physics;

/// <summary>
/// The poses of the bodies of one <see cref="RigidBody"/>, picked out of a <see cref="PhysicsWorld"/>. The world
/// publishes all its bodies in one buffer, which can only be drawn with one mesh; this gives each description a
/// buffer of its own, so a chassis and its wheels, or two fractured objects, can be drawn separately.
/// </summary>
[Guid("d050e786-b744-4d12-a43d-5ed53fa41950")]
internal sealed class RigidBodyPoints : Instance<RigidBodyPoints>
{
    [Output(Guid = "49e195a2-8c7c-4a02-98c3-bd810ebdaea1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> Points = new();

    [Output(Guid = "bbabd4a8-f55f-45f9-b489-133c79c9461f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Count = new();

    public RigidBodyPoints()
    {
        Points.UpdateAction += Update;
        Count.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Pulling the world's buffer is what makes the world step this frame and publish the ranges read below.
        var worldPoints = World.GetValue(context);
        var body = Body.GetValue(context);

        var source = body?.PublishedPoints;
        var start = body?.PublishedStart ?? 0;
        var count = body?.PublishedCount ?? 0;
        // The published ranges must belong to the buffer pulled above; a description feeding two worlds
        // would otherwise be drawn with whichever world evaluated last.
        if (worldPoints == null || body?.PublishedBuffer != worldPoints || source == null || count <= 0 || start < 0 || start + count > source.Length
            || ResourceManager.Device == null || !EnsureBuffer(count))
        {
            Points.Value = null;
            Points.DirtyFlag.Invalidate();
            Count.Value = 0;
            Count.DirtyFlag.Invalidate();
            return;
        }

        Array.Copy(source, start, _points, 0, count);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(_points, _buffer);
        Points.Value = _bufferWithViews;
        Points.DirtyFlag.Invalidate();
        Count.Value = count;
        Count.DirtyFlag.Invalidate();
    }

    private bool EnsureBuffer(int count)
    {
        var totalBytes = count * Point.Stride;
        if (_buffer != null && _buffer.Description.SizeInBytes == totalBytes)
            return true;

        ReleaseBuffer();
        ResourceManager.SetupStructuredBuffer(totalBytes, Point.Stride, ref _buffer);
        if (_buffer == null)
            return false;

        ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);
        if (_bufferWithViews.Srv == null || _bufferWithViews.Uav == null)
        {
            ReleaseBuffer();
            return false;
        }

        _bufferWithViews.Buffer = _buffer;
        _points = new Point[count];
        return true;
    }

    private void ReleaseBuffer()
    {
        _bufferWithViews.Srv?.Dispose();
        _bufferWithViews.Uav?.Dispose();
        _bufferWithViews.Srv = null;
        _bufferWithViews.Uav = null;
        _bufferWithViews.Buffer = null;
        _buffer?.Dispose();
        _buffer = null;
        _points = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseBuffer();

        base.Dispose(disposing);
    }

    private Point[] _points;
    private BufferWithViews _bufferWithViews = new();
    private Buffer _buffer;

    [Input(Guid = "f006ea17-a6c5-45ad-8657-74b88873061a")]
    public readonly InputSlot<BufferWithViews> World = new();

    [Input(Guid = "227ac807-1515-43b7-97fe-dade52819264")]
    public readonly InputSlot<PhysicsBody> Body = new();
}
