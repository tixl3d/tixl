using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.DataTypes;
using T3.Core.Utils;

namespace Lib.image.analyze.blobtrack;

[Guid("38000677-49F7-48D7-A576-FA081AFC29D6")]
internal sealed class BlobDataToBoxPoints : Instance<BlobDataToBoxPoints>
{
    [Output(Guid = "5C91E968-B6B9-4D78-A213-215720318DFE")]
    public readonly Slot<StructuredList> Points = new();

    [Output(Guid = "89D92E71-0973-49EB-AB9D-87E97A35ED5D")]
    public readonly Slot<BufferWithViews> GpuBuffer = new();

    [Input(Guid = "B97C5F9F-76E5-49A8-96A1-9B5E3F2FE2AD")]
    public readonly InputSlot<BufferWithViews> BlobData = new();

    [Input(Guid = "956CDFD4-1616-4639-BD04-E1A63BCF01A1")]
    public readonly InputSlot<Vector4> Color = new(new Vector4(0, 1, 0, 1));

    [Input(Guid = "6CE772FB-94E5-41BB-8A49-8E40B4BE139D")]
    public readonly InputSlot<float> LineWidth = new(0.01f);

    [Input(Guid = "9B5A4D84-80FF-48DC-9A24-D905906B3CFD")]
    public readonly InputSlot<float> Scale = new(1.0f);

    [Input(Guid = "E6B707F4-7B2E-4BDE-B2D8-2B62F192A8E5")]
    public readonly InputSlot<float> AspectRatio = new(1.0f);

    public BlobDataToBoxPoints()
    {
        Points.UpdateAction += Update;
        GpuBuffer.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var blobData = BlobData.GetValue(context);
        if (blobData == null || blobData.Buffer == null || blobData.Buffer.Description.StructureByteStride < BlobInfo.Stride)
        {
            Points.Value = new StructuredList<Point>(0);
            GpuBuffer.Value = null;
            return;
        }

        var color = Color.GetValue(context);
        var lineWidth = LineWidth.GetValue(context);
        var scale = Scale.GetValue(context);
        var aspect = AspectRatio.GetValue(context);
        if (aspect < 0.0001f)
            aspect = 1.0f;

        var blobs = ReadBlobs(blobData);
        var blobCount = blobs.Length;
        var pointCount = blobCount * 6;

        if (_pointList.NumElements != pointCount)
            _pointList.SetLength(pointCount);

        var halfWidth = scale * aspect;
        var halfHeight = scale;

        for (var i = 0; i < blobCount; i++)
        {
            var blob = blobs[i];
            var cx = (blob.CenterX - 0.5f) * 2.0f * halfWidth;
            var cy = (0.5f - blob.CenterY) * 2.0f * halfHeight;
            var w = blob.Width * 2.0f * halfWidth;
            var h = blob.Height * 2.0f * halfHeight;

            var left = cx - w * 0.5f;
            var right = cx + w * 0.5f;
            var top = cy - h * 0.5f;
            var bottom = cy + h * 0.5f;

            var index = i * 6;
            _pointList.TypedElements[index] = MakePoint(left, top, color, lineWidth);
            _pointList.TypedElements[index + 1] = MakePoint(right, top, color, lineWidth);
            _pointList.TypedElements[index + 2] = MakePoint(right, bottom, color, lineWidth);
            _pointList.TypedElements[index + 3] = MakePoint(left, bottom, color, lineWidth);
            _pointList.TypedElements[index + 4] = MakePoint(left, top, color, lineWidth);
            _pointList[index + 5] = Point.Separator();
        }

        Points.Value = _pointList;
        GpuBuffer.Value = BuildGpuBuffer(_pointList, ref _gpuBufferWithViews);
    }

    private static Point MakePoint(float x, float y, Vector4 color, float lineWidth)
    {
        return new Point
        {
            Position = new Vector3(x, y, 0),
            F1 = lineWidth,
            Orientation = Quaternion.Identity,
            Color = color,
            Scale = Vector3.One,
            F2 = 1,
        };
    }

    private static BlobInfo[] ReadBlobs(BufferWithViews blobData)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;
        var buffer = blobData.Buffer;
        var stride = buffer.Description.StructureByteStride;
        var elementCount = buffer.Description.SizeInBytes / stride;
        if (elementCount <= 0)
            return Array.Empty<BlobInfo>();

        using var staging = new SharpDX.Direct3D11.Buffer(device, new BufferDescription
        {
            Usage = ResourceUsage.Staging,
            SizeInBytes = buffer.Description.SizeInBytes,
            StructureByteStride = stride,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.BufferStructured,
        });

        context.CopyResource(buffer, staging);
        var dataBox = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            using var stream = new DataStream(dataBox.DataPointer, dataBox.RowPitch, true, false);
            return stream.ReadRange<BlobInfo>(elementCount);
        }
        finally
        {
            context.UnmapSubresource(staging, 0);
        }
    }

    private static BufferWithViews BuildGpuBuffer(StructuredList<Point> pointList, ref BufferWithViews bufferWithViews)
    {
        var elementCount = pointList.NumElements;
        if (elementCount <= 0)
            return null;

        bufferWithViews ??= new BufferWithViews();

        var sizeInBytes = Point.Stride * elementCount;
        ResourceManager.SetupStructuredBuffer(pointList.TypedElements, sizeInBytes, Point.Stride, ref bufferWithViews.Buffer);

        ResourceManager.CreateStructuredBufferSrv(bufferWithViews.Buffer, ref bufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(bufferWithViews.Buffer, UnorderedAccessViewBufferFlags.None, ref bufferWithViews.Uav);
        return bufferWithViews;
    }

    private readonly StructuredList<Point> _pointList = new(5);
    private BufferWithViews _gpuBufferWithViews;
}
