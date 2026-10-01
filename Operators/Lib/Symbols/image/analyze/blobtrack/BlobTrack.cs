using System.Runtime.InteropServices;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.DataTypes;
using T3.Core.Utils;

namespace Lib.image.analyze.blobtrack;

[StructLayout(LayoutKind.Explicit, Size = Stride)]
public struct BlobInfo
{
    [FieldOffset(0)]
    public int Id;

    [FieldOffset(4)]
    public float CenterX;

    [FieldOffset(8)]
    public float CenterY;

    [FieldOffset(12)]
    public float Width;

    [FieldOffset(16)]
    public float Height;

    [FieldOffset(20)]
    public float Area;

    [FieldOffset(24)]
    public float Padding0;

    [FieldOffset(28)]
    public float Padding1;

    public const int Stride = 32;
}

[Guid("a3e7b1c4-5d2f-4896-b0e1-9c3f6a8d2e47")]
[ExportDependencies("OpenCvSharpExtern.dll")]
internal sealed class BlobTrack : Instance<BlobTrack>
{
    [Input(Guid = "b1c2d3e4-f5a6-7890-abcd-ef0123456789")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "c2d3e4f5-a6b7-8901-bcde-f01234567890")]
    public readonly InputSlot<Texture2D> Background = new();

    [Input(Guid = "d3e4f5a6-b7c8-9012-cdef-012345678901")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "e4f5a6b7-c8d9-0123-defa-123456789012")]
    public readonly InputSlot<float> MinBlobSize = new();

    [Input(Guid = "f5a6b7c8-d9e0-1234-efab-234567890123")]
    public readonly InputSlot<float> MaxBlobSize = new();

    [Input(Guid = "a6b7c8d9-e0f1-2345-fabc-345678901234")]
    public readonly InputSlot<float> MaxMoveDistance = new();

    [Input(Guid = "b7c8d9e0-f1a2-3456-abcd-456789012345", MappedType = typeof(MonoSources))]
    public readonly InputSlot<int> MonoSource = new();

    [Input(Guid = "c8d9e0f1-a2b3-4567-bcde-567890123456", MappedType = typeof(OverlayModes))]
    public readonly InputSlot<int> Overlay = new();

    [Input(Guid = "4eb740e3-d5ba-4ebc-9707-018fb9d8df7e")]
    public readonly InputSlot<Vector4> BoundsColor = new(new Vector4(0, 1, 0, 1));

    [Input(Guid = "3737c853-81f0-4fd9-9f82-207c2fe1a856")]
    public readonly InputSlot<Vector4> LabelColor = new(Vector4.One);

    [Input(Guid = "1496761b-6455-45a1-a61a-303119b3a1e3")]
    public readonly InputSlot<int> BoundsThickness = new(2);

    [Input(Guid = "3c39d125-8cb4-4e84-a51d-d1c88721c740")]
    public readonly InputSlot<float> LabelSize = new(0.5f);

    [Input(Guid = "dc4a0ab8-e412-4640-8ad3-de254039d9b1")]
    public readonly InputSlot<int> LabelThickness = new(1);

    [Input(Guid = "76d42773-ddbd-497a-8c17-927b09ccc6a9")]
    public readonly InputSlot<string> LabelPrefix = new("ID:");

    [Input(Guid = "64a3a4f5-420c-414d-80ef-5e0f33bb726f", MappedType = typeof(IdFormats))]
    public readonly InputSlot<int> IdFormat = new();

    [Input(Guid = "f4885527-c56f-4040-ad4e-7d5351b19d08", MappedType = typeof(LabelContentModes))]
    public readonly InputSlot<int> LabelContent = new();

    [Input(Guid = "0b4aeb69-f133-4ade-8708-53245d99e728")]
    public readonly InputSlot<float> DetectScale = new(1.0f);

    [Input(Guid = "79ea740c-5c24-4969-b58f-724796d9ade2")]
    public readonly InputSlot<bool> AntiAlias = new(false);

    [Input(Guid = "d9e0f1a2-b3c4-5678-cdef-678901234567")]
    public readonly InputSlot<bool> Reset = new();

    [Output(Guid = "e0f1a2b3-c4d5-6789-defa-789012345678")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Output(Guid = "f1a2b3c4-d5e6-7890-efab-890123456789")]
    public readonly Slot<BufferWithViews> BlobData = new();

    [Output(Guid = "a2b3c4d5-e6f7-8901-fabc-901234567890")]
    public readonly Slot<int> NumBlobs = new();

    private enum MonoSources
    {
        Luminance,
        Red,
        Green,
        Blue,
        Alpha,
    }

    private enum OverlayModes
    {
        BoundsOnly,
        LabelsOnly,
        BoundsAndLabels,
        Off,
    }

    private enum IdFormats
    {
        Decimal,
        HexadecimalUpper,
        HexadecimalLower,
        Binary,
    }

    private enum LabelContentModes
    {
        IdAndNumber,
        IdOnly,
        NumberOnly,
    }

    private Texture2D _outputTexture;
    private BufferWithViews _blobBuffer;
    private bool _resetTriggered;
    private int _nextBlobId = 1;

    private readonly List<BlobInfo> _currentBlobs = new();
    private readonly List<BlobInfo> _previousBlobs = new();
    private readonly List<OpenCvSharp.Point2f> _blobCenters = new();
    private readonly List<float> _blobAreas = new();
    private readonly List<OpenCvSharp.Rect> _blobBounds = new();
    private readonly List<bool> _matchedPrev = new();
    private readonly List<bool> _matchedCurr = new();

    private SimpleBlobDetector _cachedDetector;
    private int _cachedMinThresh;
    private int _cachedMaxThresh;
    private float _cachedMinArea;
    private float _cachedMaxArea;

    private Texture2D _cachedBgTexture;
    private Mat _cachedBgGray;

    public BlobTrack()
    {
        TextureOutput.UpdateAction = Update;
        BlobData.UpdateAction = Update;
        NumBlobs.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        if (MathUtils.WasTriggered(Reset.GetValue(context), ref _resetTriggered))
        {
            _previousBlobs.Clear();
            _currentBlobs.Clear();
            _nextBlobId = 1;
        }

        var inputTex = Image.GetValue(context);
        if (inputTex == null)
        {
            TextureOutput.Value = null;
            return;
        }

        using var inputMat = ConvertTextureToMat(inputTex);
        if (inputMat == null || inputMat.Empty())
        {
            TextureOutput.Value = null;
            return;
        }

        var threshold = Threshold.GetValue(context);
        var minSize = MinBlobSize.GetValue(context);
        var maxSize = MaxBlobSize.GetValue(context);
        var maxMove = MaxMoveDistance.GetValue(context);
        var monoSource = MonoSource.GetValue(context);
        var overlay = Overlay.GetValue(context);
        var detectScale = Math.Clamp(DetectScale.GetValue(context), 0.001f, 1.0f);
        var boundsColor = BoundsColor.GetValue(context);
        var labelColor = LabelColor.GetValue(context);
        var boundsThickness = BoundsThickness.GetValue(context);
        var labelSize = LabelSize.GetValue(context);
        var labelThickness = LabelThickness.GetValue(context);
        var labelPrefix = LabelPrefix.GetValue(context);
        var idFormat = IdFormat.GetValue(context);
        var labelContent = LabelContent.GetValue(context);
        var antiAlias = AntiAlias.GetValue(context);

        using var gray = ConvertToGray(inputMat, monoSource);

        var detectWidth = inputMat.Width;
        var detectHeight = inputMat.Height;
        if (detectScale < 1.0f)
        {
            detectWidth = Math.Max(1, (int)Math.Round(inputMat.Width * detectScale));
            detectHeight = Math.Max(1, (int)Math.Round(inputMat.Height * detectScale));
            if (gray.Width != detectWidth || gray.Height != detectHeight)
                Cv2.Resize(gray, gray, new OpenCvSharp.Size(detectWidth, detectHeight), 0, 0, InterpolationFlags.Area);
        }

        var detectArea = detectWidth * detectHeight;
        var minAreaPixels = Math.Max(1, minSize * detectArea);
        var maxAreaPixels = Math.Max(minAreaPixels + 1, maxSize * detectArea);

        var bgTex = Background.GetValue(context);
        if (bgTex != null)
        {
            DetectWithBackgroundSubtraction(gray, bgTex, threshold, minAreaPixels, maxAreaPixels);
        }
        else
        {
            DetectWithSimpleBlobDetector(gray, threshold, minAreaPixels, maxAreaPixels);
        }

        TrackBlobs(maxMove);

        if (overlay == (int)OverlayModes.Off)
        {
            TextureOutput.Value = inputTex;
        }
        else
        {
            DrawBlobOverlays(inputMat, (OverlayModes)overlay, boundsColor, labelColor, boundsThickness, labelSize, labelThickness, labelPrefix, idFormat, labelContent, antiAlias);
            UploadMatToTexture(inputMat, ref _outputTexture);
            TextureOutput.Value = _outputTexture;
        }

        UpdateBlobBuffer();
        NumBlobs.Value = _currentBlobs.Count;

        _previousBlobs.Clear();
        foreach (var blob in _currentBlobs)
            _previousBlobs.Add(blob);
    }

    private void DetectWithBackgroundSubtraction(Mat gray, Texture2D bgTex, float threshold, double minArea, double maxArea)
    {
        minArea = Math.Max(1, minArea);
        maxArea = Math.Max(minArea + 1, maxArea);

        if (_cachedBgGray == null || _cachedBgGray.IsDisposed || !ReferenceEquals(_cachedBgTexture, bgTex))
        {
            using var bgMat = ConvertTextureToMat(bgTex);
            if (bgMat == null || bgMat.Empty()) return;

            _cachedBgGray?.Dispose();
            _cachedBgGray = new Mat();
            Cv2.CvtColor(bgMat, _cachedBgGray, ColorConversionCodes.BGR2GRAY);
            _cachedBgTexture = bgTex;
        }

        using var bgGray = new Mat();
        if (_cachedBgGray.Size() != gray.Size())
            Cv2.Resize(_cachedBgGray, bgGray, gray.Size());
        else
            _cachedBgGray.CopyTo(bgGray);

        using var diff = new Mat();
        Cv2.Absdiff(gray, bgGray, diff);

        using var thresh = new Mat();
        var threshValue = (int)(threshold * 255);
        Cv2.Threshold(diff, thresh, threshValue, 255, ThresholdTypes.Binary);

        Cv2.FindContours(thresh, out var contours, out var _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        _currentBlobs.Clear();
        _blobCenters.Clear();
        _blobAreas.Clear();
        _blobBounds.Clear();

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < minArea || area > maxArea) continue;

            var rect = Cv2.BoundingRect(contour);
            var cx = (float)(rect.X + rect.Width * 0.5) / gray.Width;
            var cy = (float)(rect.Y + rect.Height * 0.5) / gray.Height;
            var w = (float)rect.Width / gray.Width;
            var h = (float)rect.Height / gray.Height;
            var normArea = (float)(area / (double)(gray.Width * gray.Height));

            _currentBlobs.Add(new BlobInfo
            {
                Id = _nextBlobId++,
                CenterX = cx,
                CenterY = cy,
                Width = w,
                Height = h,
                Area = normArea,
            });

            _blobCenters.Add(new OpenCvSharp.Point2f(cx, cy));
            _blobAreas.Add(normArea);
            _blobBounds.Add(rect);
        }
    }

    private void DetectWithSimpleBlobDetector(Mat gray, float threshold, double minArea, double maxArea)
    {
        minArea = Math.Max(1, minArea);
        maxArea = Math.Max(minArea + 1, maxArea);

        var minThresh = 10;
        var maxThresh = Math.Max(minThresh + 1, (int)(threshold * 255));
        var minAreaF = (float)minArea;
        var maxAreaF = (float)maxArea;

        if (_cachedDetector == null || _cachedDetector.IsDisposed ||
            _cachedMinThresh != minThresh || _cachedMaxThresh != maxThresh ||
            _cachedMinArea != minAreaF || _cachedMaxArea != maxAreaF)
        {
            var parameters = new SimpleBlobDetector.Params
            {
                MinThreshold = minThresh,
                MaxThreshold = maxThresh,
                ThresholdStep = 10,
                FilterByArea = true,
                MinArea = minAreaF,
                MaxArea = maxAreaF,
                FilterByCircularity = false,
                FilterByConvexity = false,
                FilterByInertia = false,
            };

            _cachedDetector?.Dispose();
            _cachedDetector = SimpleBlobDetector.Create(parameters);
            _cachedMinThresh = minThresh;
            _cachedMaxThresh = maxThresh;
            _cachedMinArea = minAreaF;
            _cachedMaxArea = maxAreaF;
        }

        var keypoints = _cachedDetector.Detect(gray);

        _currentBlobs.Clear();
        _blobCenters.Clear();
        _blobAreas.Clear();
        _blobBounds.Clear();

        foreach (var kp in keypoints)
        {
            var cx = (float)kp.Pt.X / gray.Width;
            var cy = (float)kp.Pt.Y / gray.Height;
            var diameter = kp.Size;
            var w = diameter / gray.Width;
            var h = diameter / gray.Height;
            var normArea = (float)(Math.PI * (diameter * 0.5) * (diameter * 0.5) / (double)(gray.Width * gray.Height));

            _currentBlobs.Add(new BlobInfo
            {
                Id = _nextBlobId++,
                CenterX = cx,
                CenterY = cy,
                Width = w,
                Height = h,
                Area = normArea,
            });

            _blobCenters.Add(new OpenCvSharp.Point2f(cx, cy));
            _blobAreas.Add(normArea);
            _blobBounds.Add(new OpenCvSharp.Rect(
                (int)(kp.Pt.X - kp.Size * 0.5),
                (int)(kp.Pt.Y - kp.Size * 0.5),
                (int)kp.Size,
                (int)kp.Size));
        }
    }

    private void TrackBlobs(float maxMoveDistance)
    {
        if (_previousBlobs.Count == 0 || _currentBlobs.Count == 0) return;

        _matchedPrev.Clear();
        _matchedCurr.Clear();
        for (int i = 0; i < _previousBlobs.Count; i++) _matchedPrev.Add(false);
        for (int i = 0; i < _currentBlobs.Count; i++) _matchedCurr.Add(false);

        var assignments = new List<(int curr, int prev, float dist)>();

        for (int c = 0; c < _currentBlobs.Count; c++)
        {
            for (int p = 0; p < _previousBlobs.Count; p++)
            {
                var dx = _currentBlobs[c].CenterX - _previousBlobs[p].CenterX;
                var dy = _currentBlobs[c].CenterY - _previousBlobs[p].CenterY;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                assignments.Add((c, p, dist));
            }
        }

        assignments.Sort((a, b) => a.dist.CompareTo(b.dist));

        foreach (var (curr, prev, dist) in assignments)
        {
            if (_matchedPrev[prev] || _matchedCurr[curr]) continue;
            if (dist > maxMoveDistance) continue;

            _matchedPrev[prev] = true;
            _matchedCurr[curr] = true;
            _currentBlobs[curr] = _currentBlobs[curr] with { Id = _previousBlobs[prev].Id };
        }
    }

    private void DrawBlobOverlays(Mat frame, OverlayModes mode,
        Vector4 boundsColor, Vector4 labelColor, int boundsThickness, float labelSize, int labelThickness,
        string labelPrefix, int idFormat, int labelContent, bool antiAlias)
    {
        if (mode == OverlayModes.Off) return;
        var drawBounds = mode == OverlayModes.BoundsOnly || mode == OverlayModes.BoundsAndLabels;
        var drawLabels = mode == OverlayModes.LabelsOnly || mode == OverlayModes.BoundsAndLabels;

        var boundsScalar = ColorToScalar(boundsColor);
        var labelScalar = ColorToScalar(labelColor);
        var contentMode = (LabelContentModes)labelContent;
        var lineType = antiAlias ? LineTypes.AntiAlias : LineTypes.Link8;

        for (int i = 0; i < _currentBlobs.Count; i++)
        {
            var blob = _currentBlobs[i];
            var x = (int)((blob.CenterX - blob.Width * 0.5f) * frame.Width);
            var y = (int)((blob.CenterY - blob.Height * 0.5f) * frame.Height);
            var w = (int)(blob.Width * frame.Width);
            var h = (int)(blob.Height * frame.Height);

            if (drawBounds)
                Cv2.Rectangle(frame, new OpenCvSharp.Rect(x, y, w, h), boundsScalar, boundsThickness);

            if (drawLabels)
            {
                var labelText = BuildLabelText(blob.Id, contentMode, labelPrefix, idFormat);
                if (!string.IsNullOrEmpty(labelText))
                    Cv2.PutText(frame, labelText,
                        new OpenCvSharp.Point(x, y - 5),
                        HersheyFonts.HersheySimplex, labelSize, labelScalar, labelThickness, lineType);
            }
        }
    }

    private static string BuildLabelText(int id, LabelContentModes contentMode, string prefix, int idFormat)
    {
        var number = FormatId(id, idFormat);
        return contentMode switch
        {
            LabelContentModes.IdOnly => prefix,
            LabelContentModes.NumberOnly => number,
            _ => prefix + number,
        };
    }

    private static string FormatId(int id, int idFormat) => (IdFormats)idFormat switch
    {
        IdFormats.Decimal => id.ToString(),
        IdFormats.HexadecimalUpper => id.ToString("X"),
        IdFormats.HexadecimalLower => id.ToString("x"),
        IdFormats.Binary => Convert.ToString(id, 2),
        _ => id.ToString(),
    };

    private static Scalar ColorToScalar(Vector4 c) => new(c.X * 255, c.Y * 255, c.Z * 255, c.W * 255);

    private void UpdateBlobBuffer()
    {
        var count = _currentBlobs.Count;
        if (count == 0)
        {
            _blobBuffer?.Dispose();
            _blobBuffer = null;
            BlobData.Value = null;
            return;
        }

        var array = _currentBlobs.ToArray();
        if (_blobBuffer == null)
        {
            _blobBuffer = new BufferWithViews();
            ResourceManager.SetupStructuredBuffer(array, BlobInfo.Stride * count, BlobInfo.Stride, ref _blobBuffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_blobBuffer.Buffer, ref _blobBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(_blobBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _blobBuffer.Uav);
        }
        else if (_blobBuffer.Buffer.Description.SizeInBytes / BlobInfo.Stride != count)
        {
            _blobBuffer.Dispose();
            _blobBuffer = new BufferWithViews();
            ResourceManager.SetupStructuredBuffer(array, BlobInfo.Stride * count, BlobInfo.Stride, ref _blobBuffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_blobBuffer.Buffer, ref _blobBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(_blobBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _blobBuffer.Uav);
        }
        else
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(array, _blobBuffer.Buffer);
        }

        BlobData.Value = _blobBuffer;
    }

    private static Mat ConvertToGray(Mat input, int monoSource)
    {
        var gray = new Mat();
        switch ((MonoSources)monoSource)
        {
            case MonoSources.Luminance:
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGRA2GRAY);
                break;
            case MonoSources.Red:
                Cv2.Split(input, out var channels);
                channels[2].CopyTo(gray);
                foreach (var ch in channels) ch.Dispose();
                break;
            case MonoSources.Green:
                Cv2.Split(input, out var channelsG);
                channelsG[1].CopyTo(gray);
                foreach (var ch in channelsG) ch.Dispose();
                break;
            case MonoSources.Blue:
                Cv2.Split(input, out var channelsB);
                channelsB[0].CopyTo(gray);
                foreach (var ch in channelsB) ch.Dispose();
                break;
            case MonoSources.Alpha:
                Cv2.Split(input, out var channelsA);
                if (channelsA.Length > 3)
                    channelsA[3].CopyTo(gray);
                else
                    Cv2.CvtColor(input, gray, ColorConversionCodes.BGRA2GRAY);
                foreach (var ch in channelsA) ch.Dispose();
                break;
            default:
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGRA2GRAY);
                break;
        }
        return gray;
    }

    private static Mat ConvertTextureToMat(Texture2D tex)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;
        var desc = tex.Description;
        var stageDesc = new Texture2DDescription
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None,
        };

        using var stageTex = new SharpDX.Direct3D11.Texture2D(device, stageDesc);
        context.CopyResource(tex, stageTex);
        var box = context.MapSubresource(stageTex, 0, MapMode.Read, MapFlags.None);
        if (box.DataPointer == IntPtr.Zero)
        {
            context.UnmapSubresource(stageTex, 0);
            return new Mat();
        }

        using var wrapped = Mat.FromPixelData(desc.Height, desc.Width, MatType.CV_8UC4, box.DataPointer, box.RowPitch);
        var result = wrapped.Clone();
        context.UnmapSubresource(stageTex, 0);
        return result;
    }

    private void UploadMatToTexture(Mat mat, ref Texture2D tex)
    {
        try
        {
            Mat uploadMat = mat;
            using var converted = new Mat();
            if (mat.Channels() == 3)
            {
                Cv2.CvtColor(mat, converted, ColorConversionCodes.BGR2BGRA);
                uploadMat = converted;
            }

            var width = uploadMat.Width;
            var height = uploadMat.Height;

            if (tex == null || tex.Description.Width != width || tex.Description.Height != height)
            {
                tex?.Dispose();
                var texDesc = new Texture2DDescription
                {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R8G8B8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                };
                tex = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, texDesc));
            }

            var dataBox = new DataBox(uploadMat.Data, (int)uploadMat.Step(), 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, tex);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to upload Mat to texture: {e.Message}", this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _cachedDetector?.Dispose();
        _cachedBgGray?.Dispose();
        _blobBuffer?.Dispose();
        _outputTexture?.Dispose();
    }
}
