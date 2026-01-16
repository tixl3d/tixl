using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Utils;
using Rectangle = System.Drawing.Rectangle;

namespace Lib.io.dmx.helpers;

[Guid("2470272f-5e56-448f-803d-15c5231353ff")]
internal sealed class Video2DPointScanner : Instance<Video2DPointScanner>
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, IncludeFields = true };

    [Input(Guid = "4a0c8d1e-1b2c-4f5a-8b9c-0d1e2f3a4b5c")]
    public readonly InputSlot<bool> ApplyCorrection = new();

    [Input(Guid = "91472e9f-5168-432b-92cd-ec826b72ffd4")]
    public readonly InputSlot<string> CalibrationPath = new();

    [Input(Guid = "89d26124-b476-443d-bdd0-82c76c0f3789")]
    public readonly InputSlot<bool> DebugMode = new();

    [Output(Guid = "882711ef-2309-4db6-85b5-b9d5dc20b8b4")]
    public readonly Slot<Texture2D> DebugTexture = new();

    [Input(Guid = "f53b09bb-3b16-46d4-a45d-f9a9b16b636e")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "572bfd98-9746-4d34-b0cd-e22a4c6d75fd")]
    public readonly InputSlot<bool> Load = new();

    [Input(Guid = "5b168a97-851b-4928-93f9-6a8a729fed3b")]
    public readonly InputSlot<bool> LoadCalibration = new();

    [Input(Guid = "bcfd87c4-6706-4447-8bbd-c069b6ebae70")]
    public readonly InputSlot<float> PixelBrightness = new();

    [Input(Guid = "1d11b92d-bf80-4f4f-9061-17fda30fb895")]
    public readonly InputSlot<int> PixelCount = new();

    // --- OUTPUTS ---
    [Output(Guid = "24c24e9c-87bc-491b-ae03-d681a07d6418")]
    public readonly Slot<BufferWithViews> PixelOutput = new Slot<BufferWithViews>();

    [Input(Guid = "3cfd6232-1506-4ba1-bcc1-1c0839a7de64")]
    public readonly InputSlot<bool> ResetScan = new();

    [Input(Guid = "7c7b7528-ca61-40ea-b5a3-ff587cffeac5")]
    public readonly InputSlot<bool> Save = new();

    [Input(Guid = "b123fade-921a-46c2-98bd-b4e78a8d4c30")]
    public readonly InputSlot<float> ScanIntervallum = new();

    [Output(Guid = "c18f3a23-81cf-4911-a201-4ef358e3f740")]
    public readonly Slot<BufferWithViews> ScannedPoints2D = new Slot<BufferWithViews>();

    [Input(Guid = "701299ed-9cfc-4eb1-8320-16c39634a5df")]
    public readonly InputSlot<bool> ScanTrigger = new();

    [Input(Guid = "58d8e589-1536-4728-8d46-de2311ce61d4")]
    public readonly InputSlot<bool> TestFullMode = new();

    [Input(Guid = "9ac8647b-0305-4bd3-a1fc-b47fdc46c297")]
    public readonly InputSlot<bool> TestPixelMode = new();

    [Input(Guid = "562a7e98-ba58-4f6b-93d7-2729030bf0e3")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "696e9554-2f01-4d15-8ac7-4fab32e96711")]
    public readonly InputSlot<Texture2D> VideoIn = new();

    private double[,] _cameraMatrix;
    private List<Vector2> _currentLed2DPositions = new List<Vector2>();
    private int _currentLedIndex = -1, _lastPixelCount = -1;
    private Texture2D _debugTexture;
    private double[] _distCoeffs;
    private bool _isCalibrated;
    private bool _isProcessing, _resetTriggered, _scanTriggered, _loadTriggered, _saveTriggered, _calibrationLoadTriggered, _isDebugEnabled;

    // --- State variables ---
    private DateTime _lastScanTime = DateTime.MinValue;
    private BufferWithViews _pixelOutputBuffer, _scannedPointsBuffer;

    public Video2DPointScanner()
    {
        PixelOutput.UpdateAction = Update;
        DebugTexture.UpdateAction = Update;
        ScannedPoints2D.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _isDebugEnabled = DebugMode.GetValue(context);
        int pixelCount = PixelCount.GetValue(context);
        float brightness = PixelBrightness.GetValue(context);

        if (pixelCount != _lastPixelCount)
        {
            InitializeCurrentScan(pixelCount);
            _lastPixelCount = pixelCount;
        }

        HandleInputsAndTriggers(context);

        var videoTex = VideoIn.GetValue(context);
        if (videoTex == null)
        {
            HandleNoVideoInput();
            return;
        }

        using var rawFrame = ConvertTextureToMat(videoTex);
        if (rawFrame == null)
        {
            HandleVideoConversionFailure();
            return;
        }

        using var frame = new Mat();
        if (_isCalibrated && ApplyCorrection.GetValue(context))
        {
            using var cameraMat = new Mat(3, 3, MatType.CV_64FC1);
            using var distMat = new Mat(1, _distCoeffs.Length, MatType.CV_64FC1);
            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                cameraMat.Set(r, c, _cameraMatrix[r, c]);
            for (var i = 0; i < _distCoeffs.Length; i++) distMat.Set(0, i, _distCoeffs[i]);
            Cv2.Undistort(rawFrame, frame, cameraMat, distMat);
        }
        else
        {
            rawFrame.CopyTo(frame);
        }

        List<Point> lightOutputPoints;
        using var debugFrame = frame.Clone();
        string statusText;
        int foundCount = _currentLed2DPositions.Count(p => p != Vector2.Zero);

        bool testFullMode = TestFullMode.GetValue(context);
        bool testPixelMode = TestPixelMode.GetValue(context);

        if (testFullMode)
        {
            _isProcessing = false;
            lightOutputPoints = GenerateLightOutput(pixelCount, -1, brightness, true);
            var allFoundPoints = FindBrightSpots(frame, true, Threshold.GetValue(context));
            DrawPointsOnFrame(debugFrame, allFoundPoints.Select(p => new Vector2(p.Position.X, p.Position.Y)).ToList(), new Scalar(255, 255, 255));
            statusText = $"Test Full Mode: Seeing {allFoundPoints.Count} points";
        }
        else if (testPixelMode)
        {
            _isProcessing = false;
            if ((DateTime.Now - _lastScanTime).TotalSeconds > ScanIntervallum.GetValue(context))
            {
                if (pixelCount > 0) _currentLedIndex = (_currentLedIndex + 1) % pixelCount;
                _lastScanTime = DateTime.Now;
            }

            lightOutputPoints = GenerateLightOutput(pixelCount, _currentLedIndex, brightness);
            var foundPoints = FindBrightSpots(frame, false, Threshold.GetValue(context));
            DrawPointsOnFrame(debugFrame, foundPoints.Select(p => new Vector2(p.Position.X, p.Position.Y)).ToList(), new Scalar(0, 255, 255));
            statusText = $"Test Pixel Mode: Cycling LED {_currentLedIndex + 1}/{pixelCount}";
        }
        else if (_isProcessing)
        {
            statusText = $"Scanning... Found {foundCount}/{pixelCount}";
            if ((DateTime.Now - _lastScanTime).TotalSeconds > ScanIntervallum.GetValue(context))
            {
                _currentLedIndex = FindNextMissingLedIndex();
                if (_currentLedIndex != -1)
                {
                    var foundPoints = FindBrightSpots(frame, false, Threshold.GetValue(context));
                    if (foundPoints.Any())
                    {
                        var newPos = new Vector2(foundPoints[0].Position.X, foundPoints[0].Position.Y);
                        _currentLed2DPositions[_currentLedIndex] = newPos;
                        if (_isDebugEnabled) Log.Debug($"Found LED {_currentLedIndex} at {newPos}", this);
                    }
                }
                else
                {
                    _isProcessing = false;
                    if (_isDebugEnabled) Log.Debug("Scan complete: All points found.", this);
                }

                _lastScanTime = DateTime.Now;
            }

            int nextLedToScan = FindNextMissingLedIndex();
            lightOutputPoints = GenerateLightOutput(pixelCount, nextLedToScan, brightness);
            DrawPointsOnFrame(debugFrame, _currentLed2DPositions, new Scalar(0, 255, 255));
        }
        else // Idle
        {
            lightOutputPoints = GenerateLightOutput(pixelCount, -1, 0);
            statusText = $"Idle. Stored points: {foundCount}/{pixelCount}.";
            if (foundCount == pixelCount && pixelCount > 0)
                statusText = $"Scan Complete. Found {foundCount}/{pixelCount}.";
            DrawPointsOnFrame(debugFrame, _currentLed2DPositions, new Scalar(0, 255, 0));
        }

        AddDebugText(debugFrame, statusText, new Scalar(0, 255, 0));
        UploadMatToTexture(debugFrame, ref _debugTexture);
        DebugTexture.Value = _debugTexture;

        UpdateGpuBufferWithPoints(ref _pixelOutputBuffer, lightOutputPoints);
        PixelOutput.Value = _pixelOutputBuffer;

        var finalPoints = new List<Point>(pixelCount);
        for (int i = 0; i < pixelCount; i++)
        {
            finalPoints.Add(new Point
                                {
                                    Position = new Vector3(_currentLed2DPositions[i].X, _currentLed2DPositions[i].Y, 0),
                                    Color = Vector4.One, F1 = i, Orientation = Quaternion.Identity
                                });
        }

        UpdateGpuBufferWithPoints(ref _scannedPointsBuffer, finalPoints);
        ScannedPoints2D.Value = _scannedPointsBuffer;
    }

    private int FindNextMissingLedIndex()
    {
        if (_currentLed2DPositions.Count == 0 || _currentLed2DPositions.All(p => p != Vector2.Zero)) return -1;
        int searchIndex = (_currentLedIndex + 1) % _currentLed2DPositions.Count;
        for (int i = 0; i < _currentLed2DPositions.Count; i++)
        {
            if (_currentLed2DPositions[searchIndex] == Vector2.Zero) return searchIndex;
            searchIndex = (searchIndex + 1) % _currentLed2DPositions.Count;
        }

        return -1;
    }

    private void HandleInputsAndTriggers(EvaluationContext context)
    {
        if (MathUtils.WasTriggered(ResetScan.GetValue(context), ref _resetTriggered))
        {
            _isProcessing = false;
            InitializeCurrentScan(PixelCount.GetValue(context));
            if (_isDebugEnabled) Log.Debug("Scan reset.", this);
        }

        if (MathUtils.WasTriggered(ScanTrigger.GetValue(context), ref _scanTriggered))
        {
            _isProcessing = !_isProcessing;
            if (_isProcessing)
            {
                InitializeCurrentScan(PixelCount.GetValue(context));
                if (_isDebugEnabled) Log.Debug("Scan started.", this);
            }
            else
            {
                if (_isDebugEnabled) Log.Debug("Scan stopped.", this);
            }
        }

        if (MathUtils.WasTriggered(Load.GetValue(context), ref _loadTriggered))
        {
            var path = FilePath.GetValue(context);
            if (string.IsNullOrEmpty(path))
            {
                Log.Warning("Load failed: File path is empty.", this);
                return;
            }

            if (_isDebugEnabled) Log.Debug($"Attempting to load positions from '{path}'...", this);
            try
            {
                string json = File.ReadAllText(path);
                var loadedPositions = JsonSerializer.Deserialize<List<Vector2>>(json, _jsonOptions) ?? new List<Vector2>();
                var currentPixelCount = PixelCount.GetValue(context);
                _currentLed2DPositions = Repeat(Vector2.Zero, currentPixelCount).ToList();
                for (int i = 0; i < Math.Min(loadedPositions.Count, currentPixelCount); i++)
                    _currentLed2DPositions[i] = loadedPositions[i];
                if (_isDebugEnabled) Log.Debug($"Loaded {loadedPositions.Count} positions successfully.", this);
            }
            catch (Exception e)
            {
                Log.Error($"Load failed: {e.Message}", this);
            }
        }

        if (MathUtils.WasTriggered(Save.GetValue(context), ref _saveTriggered))
        {
            var path = FilePath.GetValue(context);
            if (string.IsNullOrEmpty(path))
            {
                Log.Warning("Save failed: File path is empty.", this);
                return;
            }

            if (_isDebugEnabled) Log.Debug($"Attempting to save {_currentLed2DPositions.Count} positions to '{path}'...", this);
            try
            {
                string json = JsonSerializer.Serialize(_currentLed2DPositions, _jsonOptions);
                File.WriteAllText(path, json);
                if (_isDebugEnabled) Log.Debug("Save successful.", this);
            }
            catch (Exception e)
            {
                Log.Error($"Save failed: {e.Message}", this);
            }
        }

        if (MathUtils.WasTriggered(LoadCalibration.GetValue(context), ref _calibrationLoadTriggered))
        {
            var path = CalibrationPath.GetValue(context);
            _isCalibrated = LoadCalibrationData(path, out _cameraMatrix, out _distCoeffs);
            if (!_isCalibrated) Log.Warning($"Failed to load calibration data from '{path}'.", this);
            else if (_isDebugEnabled) Log.Debug($"Successfully loaded calibration from '{path}'.", this);
        }
    }

    #region Helper Methods
    private void InitializeCurrentScan(int pixelCount)
    {
        _currentLedIndex = -1;
        _currentLed2DPositions = Repeat(Vector2.Zero, pixelCount).ToList();
    }

    public static bool LoadCalibrationData(string filepath, out double[,] cameraMatrix, out double[] distCoeffs)
    {
        cameraMatrix = null;
        distCoeffs = null;
        if (!File.Exists(filepath))
            return false;

        try
        {
            var lines = File.ReadAllLines(filepath);
            if (lines.Length < 6 || lines[0] != "CameraMatrix" || lines[4] != "DistortionCoeffs") return false;
            cameraMatrix = new double[3, 3];
            for (var r = 0; r < 3; r++)
            {
                var v = lines[r + 1].Split(',');
                for (var c = 0; c < 3; c++) cameraMatrix[r, c] = double.Parse(v[c], CultureInfo.InvariantCulture);
            }

            var dv = lines[5].Split(',');
            distCoeffs = new double[dv.Length];
            for (var i = 0; i < dv.Length; i++) distCoeffs[i] = double.Parse(dv[i], CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load calibration file '{filepath}': {e.Message}");
            return false;
        }
    }

    private void HandleNoVideoInput()
    {
        UpdateGpuBufferWithPoints(ref _pixelOutputBuffer, new List<Point>());
        UpdateGpuBufferWithPoints(ref _scannedPointsBuffer, new List<Point>());
        PixelOutput.Value = _pixelOutputBuffer;
        ScannedPoints2D.Value = _scannedPointsBuffer;
    }

    private void HandleVideoConversionFailure()
    {
        UpdateGpuBufferWithPoints(ref _pixelOutputBuffer, new List<Point>());
        UpdateGpuBufferWithPoints(ref _scannedPointsBuffer, new List<Point>());
        PixelOutput.Value = _pixelOutputBuffer;
        ScannedPoints2D.Value = _scannedPointsBuffer;
    }

    private List<Point> FindBrightSpots(Mat frame, bool findAll, float threshold)
    {
        var points = new List<Point>();
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        using var thresh = new Mat();
        Cv2.Threshold(gray, thresh, (int)(threshold * 255), 255, ThresholdTypes.Binary);
        Cv2.FindContours(thresh, out var contours, out var _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return points;

        if (findAll)
        {
            foreach (var c in contours)
            {
                if (Cv2.ContourArea(c) > 2)
                {
                    var m = Cv2.Moments(c);
                    if (m.M00 != 0)
                        points.Add(new Point
                                       {
                                           Position = new Vector3(((float)(m.M10 / m.M00) / frame.Width - 0.5f) * 2f,
                                                                  ((float)(m.M01 / m.M00) / frame.Height - 0.5f) * -2f, 0),
                                           F1 = 1
                                       });
                }
            }
        }
        else
        {
            var largest = contours.OrderByDescending(c => Cv2.ContourArea(c)).FirstOrDefault();
            if (largest != null && Cv2.ContourArea(largest) > 2)
            {
                var m = Cv2.Moments(largest);
                if (m.M00 != 0)
                    points.Add(new Point
                                   {
                                       Position = new Vector3(((float)(m.M10 / m.M00) / frame.Width - 0.5f) * 2f,
                                                              ((float)(m.M01 / m.M00) / frame.Height - 0.5f) * -2f, 0),
                                       F1 = 1
                                   });
            }
        }

        return points;
    }

    private void DrawPointsOnFrame(Mat frame, List<Vector2> points, Scalar color)
    {
        foreach (var v in points.Where(v => v != Vector2.Zero))
            Cv2.Circle(frame, new OpenCvSharp.Point((int)((v.X / 2f + 0.5f) * frame.Width), (int)((v.Y / -2f + 0.5f) * frame.Height)), 5, color, -1);
    }

    private void AddDebugText(Mat frame, string text, Scalar color)
    {
        Cv2.PutText(frame, text, new OpenCvSharp.Point(10, 30), HersheyFonts.HersheySimplex, 0.7, color, 2, LineTypes.AntiAlias);
    }

    private void UpdateGpuBufferWithPoints(ref BufferWithViews buffer, List<Point> list)
    {
        var num = list?.Count ?? 0;
        if (num != (buffer?.Buffer.Description.SizeInBytes / Point.Stride ?? 0))
        {
            buffer?.Dispose();
            buffer = null;
        }

        if (num == 0) return;
        if (list != null)
        {
            var array = list.ToArray();
            if (buffer == null)
            {
                buffer = new BufferWithViews();
                ResourceManager.SetupStructuredBuffer(array, Point.Stride * num, Point.Stride, ref buffer.Buffer);
                ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
                ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
            }
            else
            {
                ResourceManager.Device.ImmediateContext.UpdateSubresource(array, buffer.Buffer);
            }
        }
    }

    private static Mat ConvertTextureToMat(Texture2D tex)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;
        var desc = tex.Description;
        var stageDesc = new Texture2DDescription
                            {
                                Width = desc.Width, Height = desc.Height, MipLevels = 1, ArraySize = 1,
                                Format = Format.B8G8R8A8_UNorm,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                                CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
                            };
        using var stageTex = new SharpDX.Direct3D11.Texture2D(device, stageDesc);
        context.CopyResource(tex, stageTex);
        var box = context.MapSubresource(stageTex, 0, MapMode.Read, MapFlags.None);
        using var matBgra = Mat.FromPixelData(desc.Height, desc.Width, MatType.CV_8UC4, box.DataPointer, box.RowPitch);
        var matBgr = new Mat();
        Cv2.CvtColor(matBgra, matBgr, ColorConversionCodes.BGRA2BGR);
        context.UnmapSubresource(stageTex, 0);
        return matBgr;
    }

    private void UploadMatToTexture(Mat mat, ref Texture2D tex)
    {
        try
        {
            using var bgraMat = new Mat();
            if (mat.Channels() == 3)
                Cv2.CvtColor(mat, bgraMat, ColorConversionCodes.BGR2BGRA);
            else
                mat.CopyTo(bgraMat);

            using var bmp = bgraMat.ToBitmap();
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            if (tex == null || tex.Description.Width != bmp.Width || tex.Description.Height != bmp.Height)
            {
                tex?.Dispose();
                var texDesc = new Texture2DDescription
                                  {
                                      Width = bmp.Width, Height = bmp.Height, MipLevels = 1, ArraySize = 1,
                                      Format = Format.B8G8R8A8_UNorm,
                                      SampleDescription = new SampleDescription(1, 0),
                                      Usage = ResourceUsage.Default,
                                      BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget
                                  };
                tex = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, texDesc, new DataRectangle(bmpData.Scan0, bmpData.Stride)));
            }
            else
            {
                ResourceManager.Device.ImmediateContext.UpdateSubresource(new DataBox(bmpData.Scan0, bmpData.Stride, 0), tex);
            }

            bmp.UnlockBits(bmpData);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to upload Mat to texture: {e.Message}", this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _pixelOutputBuffer?.Dispose();
        _scannedPointsBuffer?.Dispose();
        _debugTexture?.Dispose();
    }

    private List<Point> GenerateLightOutput(int count, int active, float bright, bool allOn = false)
    {
        var list = new List<Point>(count);
        var activeC = new Vector4(bright, bright, bright, 1);
        var blackC = new Vector4(0, 0, 0, 1);
        for (int i = 0; i < count; i++)
        {
            list.Add(new Point { Position = Vector3.Zero, Color = (allOn || i == active) ? activeC : blackC, F1 = 1, Orientation = Quaternion.Identity });
        }

        return list;
    }
    #endregion
}