using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SharpDX;
using SharpDX.Direct3D11;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using Size = OpenCvSharp.Size;

namespace Lib.io.video
{
    [Guid("7b4d3c2a-5b16-4b2a-8f3a-7e8c9d0b1a2b")]
    public class CameraCalibrator : Instance<CameraCalibrator>, ICustomDropdownHolder
    {
        public enum Modes
        {
            Passthrough,
            Calibration,
            LensCorrected
        }

        public enum UndistortMethods
        {
            Undistort,
            Remap
        }

        private const int MinCalibrationSamples = 15;

        [Input(Guid = "2A3B4C5D-6E7F-4B2C-9DAE-1F2A3B4C5D6E")]
        public readonly InputSlot<float> Alpha = new(1.0f);

        [Input(Guid = "E7F1F6C5-3F0E-4E8F-2F5F-6F3F7F1E9D6F")]
        public readonly InputSlot<int> BorderInSquares = new(1);

        [Input(Guid = "B0F4F9F8-6F3F-4F1F-5F8F-9F6F0F4F2F9F")]
        public readonly InputSlot<bool> Calibrate = new();

        [Input(Guid = "A9F3F8E7-5F2F-4F0F-4F7F-8F5F9F3F1F8F")]
        public readonly InputSlot<bool> CaptureImage = new();

        [Output(Guid = "3A4B5C6D-7E8F-4C3D-AFBF-2F3A4B5C6D7E")]
        public readonly Slot<Texture2D> CheckerboardTexture = new();

        [Input(Guid = "C5D9F4A3-1E8C-4C6F-0D3E-4D1F5F9C7B4E")]
        public readonly InputSlot<Int2> ChessboardSize = new(new Int2(7, 6));

        [Input(Guid = "D6E0F5B4-2F9D-4D7F-1E4F-5E2F6F0D8C5F")]
        public readonly InputSlot<Int2> DisplayResolution = new(new Int2(1920, 1080));

        [Input(Guid = "D2F6F1F0-8F5F-4F3F-7F0F-1F8F2F6F4F1F")]
        public readonly InputSlot<string> FilePath = new("calibration.dat");

        [Output(Guid = "B6F0F5F4-2F9F-4F7F-1F4F-5F2F6F0F8F5F")]
        public readonly Slot<bool> IsCalibrated = new();

        [Input(Guid = "F4F8F3F2-0F7F-4F5F-9F2F-3F0F4F8F6F3F")]
        public readonly InputSlot<bool> Load = new();

        [Input(Guid = "B4C8E3F2-0D7F-4B5E-9C2D-3C0F4E8B6A3D", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new((int)Modes.Passthrough);

        [Input(Guid = "C1F5F0F9-7F4F-4F2F-6F9F-0F7F1F5F3F0F")]
        public readonly InputSlot<bool> Reset = new();

        [Input(Guid = "E3F7F2F1-9F6F-4F4F-8F1F-2F9F3F7F5F2F")]
        public readonly InputSlot<bool> Save = new();

        [Input(Guid = "F8F2F7D6-4F1F-4F9F-3F6F-7F4F8F2F0E7F")]
        public readonly InputSlot<float> SquareInMm = new(25.0f);

        [Output(Guid = "C7F1F6F5-3F0F-4F8F-2F5F-6F3F7F1F9F6F")]
        public readonly Slot<string> StatusMessage = new();

        [Input(Guid = "A3F7D2B1-9C8E-4A6D-8F1C-2B9E3D7A5C2E")]
        public readonly InputSlot<Texture2D> TextureIn = new();

        [Output(Guid = "A5F9F4F3-1F8F-4F6F-0F3F-4F1F5F9F7F4F")]
        public readonly Slot<Texture2D> TextureOut = new();

        [Input(Guid = "1A2B3C4D-5E6F-4A1B-8C9D-0E1F2A3B4C5D", MappedType = typeof(UndistortMethods))]
        public readonly InputSlot<int> UndistortMethod = new((int)UndistortMethods.Undistort);

        private readonly List<Point2f[]> _calibrationImagePoints = new();
        private readonly List<Point3f[]> _calibrationObjectPoints = new();
        private double[,] _cameraMatrix = new double[3, 3];
        private double[] _distCoeffs = new double[8];
        private Size _frameSize;
        private bool _isCalibrated, _captureTriggered, _calibrateTriggered, _saveTriggered, _loadTriggered, _resetTriggered;
        private int _lastBorderSize;
        private Size _lastPatternSize, _lastDisplayResolution;
        private Texture2D _textureOut, _checkerboardTexture;

        public CameraCalibrator()
        {
            CheckerboardTexture.UpdateAction = Update;
            TextureOut.UpdateAction = Update;
            IsCalibrated.UpdateAction = Update;
            StatusMessage.UpdateAction = Update;
        }

        private void Update(EvaluationContext context)
        {
            HandleTriggers(context);
            var mode = (Modes)Mode.GetValue(context);

            var patternSizeVec2 = ChessboardSize.GetValue(context);
            var displayResolutionInt2 = DisplayResolution.GetValue(context);
            var border = BorderInSquares.GetValue(context);

            var patternSize = new Size(patternSizeVec2.X, patternSizeVec2.Y);
            var displayResolution = new Size(displayResolutionInt2.X, displayResolutionInt2.Y);

            var needsRegen = _checkerboardTexture == null || patternSize != _lastPatternSize || displayResolution != _lastDisplayResolution ||
                             border != _lastBorderSize;
            if (needsRegen)
            {
                GenerateCheckerboard(patternSize, displayResolution, border);
                _lastPatternSize = patternSize;
                _lastDisplayResolution = displayResolution;
                _lastBorderSize = border;
            }

            CheckerboardTexture.Value = _checkerboardTexture;

            var textureInput = TextureIn.GetValue(context);
            if (textureInput == null)
            {
                StatusMessage.Value = "Please connect a camera feed.";
                return;
            }

            using var frame = ConvertTextureToMat(textureInput);
            if (frame == null) return;

            _frameSize = new Size(frame.Width, frame.Height);

            Mat processedFrame = null;
            try
            {
                switch (mode)
                {
                    case Modes.Passthrough:
                        processedFrame = frame.Clone();
                        StatusMessage.Value = "Passthrough mode.";
                        break;

                    case Modes.Calibration:
                        processedFrame = ProcessCalibration(frame, context);
                        break;

                    case Modes.LensCorrected:
                        if (_isCalibrated)
                        {
                            processedFrame = ProcessLensCorrection(frame, context);
                            StatusMessage.Value = "Applying lens correction.";
                        }
                        else
                        {
                            processedFrame = frame.Clone();
                            StatusMessage.Value = "No calibration data loaded. Output is uncorrected.";
                        }

                        break;
                }

                if (processedFrame != null)
                {
                    UploadMatToTexture(processedFrame, ref _textureOut);
                    TextureOut.Value = _textureOut;
                }
                else
                {
                    TextureOut.Value = null; // Skip upload to reduce render load
                }

                IsCalibrated.Value = _isCalibrated;
            }
            finally
            {
                processedFrame?.Dispose();
            }
        }

        private Mat ProcessLensCorrection(Mat frame, EvaluationContext context)
        {
            var undistortMethod = (UndistortMethods)UndistortMethod.GetValue(context);
            var alpha = Alpha.GetValue(context);

            using var cameraMat = new Mat(3, 3, MatType.CV_64FC1);
            using var distMat = new Mat(1, _distCoeffs.Length, MatType.CV_64FC1);
            for (int r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                cameraMat.At<double>(r, c) = _cameraMatrix[r, c];
            for (int i = 0; i < _distCoeffs.Length; i++)
                distMat.At<double>(i) = _distCoeffs[i];

            using var newCameraMat = Cv2.GetOptimalNewCameraMatrix(cameraMat, distMat, _frameSize, alpha, _frameSize, out var roi);
            if (newCameraMat.Empty())
            {
                Log.Warning("[Calibrator] Failed to compute optimal new camera matrix.", this);
                return frame.Clone();
            }

            Mat processedFrame;
            try
            {
                switch (undistortMethod)
                {
                    case UndistortMethods.Undistort:
                    {
                        using var tempFrame = new Mat();
                        Cv2.Undistort(frame, tempFrame, cameraMat, distMat, newCameraMat);
                        if (roi.Width > 0 && roi.Height > 0)
                        {
                            using var cropped = new Mat(tempFrame, roi);
                            processedFrame = cropped.Clone();
                        }
                        else
                        {
                            processedFrame = tempFrame.Clone();
                        }

                        break;
                    }

                    case UndistortMethods.Remap:
                    {
                        using var mapX = new Mat();
                        using var mapY = new Mat();
                        Cv2.InitUndistortRectifyMap(cameraMat, distMat, null!, newCameraMat, _frameSize, MatType.CV_32FC1, mapX, mapY);
                        using var tempFrame = new Mat();
                        Cv2.Remap(frame, tempFrame, mapX, mapY);
                        if (roi.Width > 0 && roi.Height > 0)
                        {
                            using var cropped = new Mat(tempFrame, roi);
                            processedFrame = cropped.Clone();
                        }
                        else
                        {
                            processedFrame = tempFrame.Clone();
                        }

                        break;
                    }

                    default:
                        processedFrame = frame.Clone();
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Calibrator] Lens correction failed: {e.Message}", this);
                processedFrame = frame.Clone();
            }

            return processedFrame;
        }

        private Mat ProcessCalibration(Mat frame, EvaluationContext context)
        {
            var boardSize = new Size(ChessboardSize.GetValue(context).X, ChessboardSize.GetValue(context).Y);
            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

            var outputFrame = frame.Clone();

            if (Cv2.FindChessboardCorners(gray, boardSize, out Point2f[] corners))
            {
                Cv2.DrawChessboardCorners(outputFrame, boardSize, corners, true);
                StatusMessage.Value = $"Pattern found! Captured {_calibrationImagePoints.Count}/{MinCalibrationSamples}. Press Capture.";

                var captureValue = CaptureImage.GetValue(context);
                if (captureValue && !_captureTriggered)
                {
                    _calibrationImagePoints.Add(corners);
                    _calibrationObjectPoints.Add(Create3DChessboardCorners(boardSize, SquareInMm.GetValue(context)));
                    Log.Debug($"[Calibrator] Captured calibration view {_calibrationImagePoints.Count}.", this);
                    _captureTriggered = true;
                    CaptureImage.SetTypedInputValue(false); // Auto-reset to make it button-like
                }
            }
            else
            {
                StatusMessage.Value =
                    $"Searching for {boardSize.Width}x{boardSize.Height} board... Captured {_calibrationImagePoints.Count}/{MinCalibrationSamples}.";
            }

            if (!CaptureImage.GetValue(context)) _captureTriggered = false;

            return outputFrame;
        }

        private void PerformCalibration()
        {
            if (_calibrationImagePoints.Count < MinCalibrationSamples)
            {
                StatusMessage.Value = $"Need at least {MinCalibrationSamples} samples, have {_calibrationImagePoints.Count}.";
                Log.Warning(StatusMessage.Value, this);
                return;
            }

            Log.Debug("[Calibrator] Starting camera calibration...", this);
            _cameraMatrix = new double[3, 3];
            _distCoeffs = new double[8]; // Standard for radial/tangential distortion (k1-k6 + p1-p2)

            var rms = Cv2.CalibrateCamera(
                                          _calibrationObjectPoints,
                                          _calibrationImagePoints,
                                          _frameSize,
                                          _cameraMatrix,
                                          _distCoeffs,
                                          out var rvecs,
                                          out var tvecs,
                                          CalibrationFlags.None | CalibrationFlags.RationalModel,
                                          new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 50, 0.0001)
                                         );

            // Check if distortion coefficients are all zero
            bool allZeroDistortion = true;
            foreach (var coeff in _distCoeffs)
            {
                if (Math.Abs(coeff) > 1e-6) // Small threshold to account for numerical precision
                {
                    allZeroDistortion = false;
                    break;
                }
            }

            if (allZeroDistortion)
            {
                Log.Warning("[Calibrator] All distortion coefficients are zero. Lens may have minimal distortion or calibration data may lack diversity. Consider capturing more varied images (different angles, edge coverage).",
                            this);
                StatusMessage.Value = $"Calibration complete! RMS error: {rms:F4}. Warning: No significant distortion detected.";
            }
            else
            {
                StatusMessage.Value =
                    $"Calibration complete! RMS error: {rms:F4}. Distortion coeffs: {string.Join(", ", _distCoeffs.Select(x => x.ToString("F6")))}";
            }

            _isCalibrated = true;
        }

        private void GenerateCheckerboard(Size patternSize, Size imageSize, int border)
        {
            if (patternSize.Width <= 0 || patternSize.Height <= 0 || imageSize.Width <= 0 || imageSize.Height <= 0) return;

            using var bmp = new Bitmap(imageSize.Width, imageSize.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White); // Explicitly qualified Color
                var totalSquaresX = patternSize.Width + 1 + border * 2;
                var totalSquaresY = patternSize.Height + 1 + (border * 2);
                var squareWidth = (float)imageSize.Width / totalSquaresX;
                var squareHeight = (float)imageSize.Height / totalSquaresY;

                for (int y = 0; y <= patternSize.Height; y++)
                {
                    for (int x = 0; x <= patternSize.Width; x++)
                    {
                        if ((x + y) % 2 == 0)
                        {
                            g.FillRectangle(Brushes.Black, (x + border) * squareWidth, (y + border) * squareHeight, squareWidth, squareHeight);
                        }
                    }
                }
            }

            UploadBitmapToTexture(bmp, ref _checkerboardTexture);
        }

        private void HandleTriggers(EvaluationContext context)
        {
            var resetValue = Reset.GetValue(context);
            if (resetValue && !_resetTriggered)
            {
                _calibrationImagePoints.Clear();
                _calibrationObjectPoints.Clear();
                _isCalibrated = false;
                StatusMessage.Value = "Calibration reset.";
                _resetTriggered = true;
                Reset.SetTypedInputValue(false);
            }
            else if (!resetValue) _resetTriggered = false;

            var calibrateValue = Calibrate.GetValue(context);
            if (calibrateValue && !_calibrateTriggered)
            {
                PerformCalibration();
                _calibrateTriggered = true;
                Calibrate.SetTypedInputValue(false);
            }
            else if (!calibrateValue) _calibrateTriggered = false;

            var saveValue = Save.GetValue(context);
            if (saveValue && !_saveTriggered)
            {
                SaveCalibrationData(FilePath.GetValue(context));
                _saveTriggered = true;
                Save.SetTypedInputValue(false);
            }
            else if (!saveValue) _saveTriggered = false;

            var loadValue = Load.GetValue(context);
            if (loadValue && !_loadTriggered)
            {
                if (LoadCalibrationData(FilePath.GetValue(context), out _cameraMatrix, out _distCoeffs))
                {
                    _isCalibrated = true;
                    bool allZeroDistortion = _distCoeffs.All(coeff => Math.Abs(coeff) < 1e-6);
                    StatusMessage.Value = allZeroDistortion
                                              ? "Calibration data loaded. Warning: No significant distortion detected."
                                              : $"Calibration data loaded. Distortion coeffs: {string.Join(", ", _distCoeffs.Select(x => x.ToString("F6")))}";
                }
                else
                {
                    StatusMessage.Value = "Failed to load calibration data.";
                }

                _loadTriggered = true;
                Load.SetTypedInputValue(false);
            }
            else if (!loadValue) _loadTriggered = false;
        }

        #region File IO & Helpers
        private Point3f[] Create3DChessboardCorners(Size boardSize, float squareInMm)
        {
            var corners = new List<Point3f>();
            for (int y = 0; y < boardSize.Height; y++)
            for (int x = 0; x < boardSize.Width; x++)
                corners.Add(new Point3f(x * squareInMm, y * squareInMm, 0));
            return corners.ToArray();
        }

        private void SaveCalibrationData(string filepath)
        {
            if (!_isCalibrated || string.IsNullOrWhiteSpace(filepath)) return;
            try
            {
                var lines = new List<string> { "CameraMatrix" };
                for (var r = 0; r < 3; r++) lines.Add($"{_cameraMatrix[r, 0]},{_cameraMatrix[r, 1]},{_cameraMatrix[r, 2]}");
                lines.Add("DistortionCoeffs");
                lines.Add(string.Join(",", _distCoeffs));
                File.WriteAllLines(filepath, lines);
                Log.Debug($"[Calibrator] Saved calibration data to {filepath}", this);
            }
            catch (Exception e)
            {
                Log.Error($"[Calibrator] Failed to save calibration: {e.Message}", this);
            }
        }
        #endregion

        #region Static Utilities
        public static bool LoadCalibrationData(string filepath, out double[,] cameraMatrix, out double[] distCoeffs)
        {
            cameraMatrix = null;
            distCoeffs = null;
            if (!File.Exists(filepath))
            {
                Log.Warning($"[Calibration] File not found: {filepath}");
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(filepath);
                cameraMatrix = new double[3, 3];
                for (var r = 0; r < 3; r++)
                {
                    var vals = lines[r + 1].Split(',');
                    for (var c = 0; c < 3; c++) cameraMatrix[r, c] = double.Parse(vals[c]);
                }

                var distVals = lines[5].Split(',');
                distCoeffs = new double[distVals.Length];
                for (var i = 0; i < distVals.Length; i++) distCoeffs[i] = double.Parse(distVals[i]);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Calibration] Failed to load file '{filepath}': {e.Message}");
                cameraMatrix = null;
                distCoeffs = null;
                return false;
            }
        }

        public static Mat ConvertTextureToMat(Texture2D t3Texture)
        {
            if (t3Texture == null) return null;
            var d3dDevice = ResourceManager.Device;
            var deviceContext = d3dDevice.ImmediateContext;
            var desc = t3Texture.Description;
            var stagingDesc = new Texture2DDescription
                                  {
                                      Width = desc.Width, Height = desc.Height, MipLevels = 1, ArraySize = 1, Format = desc.Format,
                                      SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                                      CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
                                  };
            using var stagingTexture = new SharpDX.Direct3D11.Texture2D(d3dDevice, stagingDesc);
            deviceContext.CopyResource(t3Texture, stagingTexture);
            var dataBox = deviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
            using var matBgra = Mat.FromPixelData(desc.Height, desc.Width, MatType.CV_8UC4, dataBox.DataPointer, dataBox.RowPitch);
            var matBgr = new Mat();
            Cv2.CvtColor(matBgra, matBgr, ColorConversionCodes.BGRA2BGR);
            deviceContext.UnmapSubresource(stagingTexture, 0);
            return matBgr;
        }

        public static void UploadMatToTexture(Mat mat, ref Texture2D texture)
        {
            if (mat == null || mat.Empty()) return;
            using var matForUpload = new Mat();
            Cv2.CvtColor(mat, matForUpload, ColorConversionCodes.BGR2BGRA);
            using var bmp = matForUpload.ToBitmap();
            UploadBitmapToTexture(bmp, ref texture);
        }

        public static void UploadBitmapToTexture(Bitmap bmp, ref Texture2D texture)
        {
            if (bmp == null) return;
            try
            {
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                if (texture == null || texture.Description.Width != bmp.Width || texture.Description.Height != bmp.Height)
                {
                    texture?.Dispose();
                    var texDesc = new Texture2DDescription
                                      {
                                          Width = bmp.Width, Height = bmp.Height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                                          SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource
                                      };
                    var dataRectangle = new DataRectangle(bmpData.Scan0, bmpData.Stride);
                    texture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, texDesc, dataRectangle));
                }
                else
                {
                    ResourceManager.Device.ImmediateContext.UpdateSubresource(new DataBox(bmpData.Scan0, bmpData.Stride, 0), texture);
                }

                bmp.UnlockBits(bmpData);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to upload texture: {e.Message}");
            }
        }
        #endregion

        #region Dropdown Implementation
        string ICustomDropdownHolder.GetValueForInput(Guid inputId)
        {
            if (inputId == ChessboardSize.Id)
                return $"{ChessboardSize.Value.X}x{ChessboardSize.Value.Y}";
            if (inputId == Mode.Id)
                return ((Modes)Mode.Value).ToString();
            if (inputId == UndistortMethod.Id)
                return ((UndistortMethods)UndistortMethod.Value).ToString();
            return string.Empty;
        }

        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
        {
            if (inputId == ChessboardSize.Id)
            {
                // Common chessboard sizes
                yield return "7x6";
                yield return "8x6";
                yield return "9x6";
                yield return "8x11";
                yield return "9x7";
                yield return "10x7";
            }
            else if (inputId == Mode.Id)
            {
                foreach (Modes m in Enum.GetValues(typeof(Modes)))
                    yield return m.ToString();
            }
            else if (inputId == UndistortMethod.Id)
            {
                foreach (UndistortMethods m in Enum.GetValues(typeof(UndistortMethods)))
                    yield return m.ToString();
            }
        }

        void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
        {
            if (inputId == ChessboardSize.Id && selected != null)
            {
                var parts = selected.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                {
                    ChessboardSize.SetTypedInputValue(new Int2(w, h));
                }
            }
            else if (inputId == Mode.Id && Enum.TryParse(typeof(Modes), selected, out var modeEnum))
            {
                Mode.SetTypedInputValue((int)(Modes)modeEnum);
            }
            else if (inputId == UndistortMethod.Id && Enum.TryParse(typeof(UndistortMethods), selected, out var methodEnum))
            {
                UndistortMethod.SetTypedInputValue((int)(UndistortMethods)methodEnum);
            }
        }
        #endregion
    }
}