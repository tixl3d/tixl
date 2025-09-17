using System.Text.RegularExpressions;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.IO;
using SharpDX.WIC;
using T3.Core.Animation;
using T3.Core.UserData;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.flow.testing;

[Guid("f92a1144-55d9-4d8e-bfa6-f80665800e25")]
internal sealed class VisualTest : Instance<VisualTest>
{
    [Output(Guid = "0B56A8D8-F7F1-4FEA-A153-B529F49B989D")]
    public readonly Slot<string> Result = new();

    public VisualTest()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var isExecution = context.IntVariables.TryGetValue(TestFrameKey, out var testFrame);
        if (!isExecution)
        {
            if (!_complainedOnce)
            {
                Log.Warning("Needs to be run with [ExecuteTests]", this);
                _complainedOnce = true;
            }

            return;
        }

        _complainedOnce = false;

        if (!context.ObjectVariables.TryGetValue(TestResultKey, out var obj2) || obj2 is not List<string> testResultList)
        {
            Log.Warning("Test Results missing?", this);
            return;
        }

        if (!context.ObjectVariables.TryGetValue(TestActionKey, out var obj) || obj is not string testAction)
        {
            Log.Warning("Test Action missing?", this);
            return;
        }

        var contextAction = testAction switch
                                {
                                    TestCommandId             => States.Testing,
                                    UpdateReferencesCommandId => States.UpdatingReferences,
                                    _                         => States.Unknown,
                                };

        if (contextAction == States.Unknown)
        {
            Log.Warning($"Unknown action {testAction}", this);
            return;
        }

        if (contextAction != _state)
        {
            Log.Debug($"Switching to {contextAction}", this);
            UpdateTestParams(context);
            _state = contextAction;
        }
        
        switch (_state)
        {
            case States.Testing:
                ConductTests(context, testResultList);
                break;

            case States.UpdatingReferences:
                UpdateAndSaveReferences(context, testResultList);
                break;
        }
    }
    

    private const string TestCommandId= "Test";
    private const string UpdateReferencesCommandId= "UpdateReferences";
    
    /// <summary>
    /// We only update test params on state change so we can reuse the same settings during multiple tests.
    /// This can be useful to iterate tests over multiple frames (e.g. while waiting at seeking within a video).
    /// </summary>
    private void UpdateTestParams(EvaluationContext context)
    {
        _testIndex = 0;
        _stepCount = StepCount.GetValue(context).Clamp(1, 100);
        _threshold = Threshold.GetValue(context);
        _timeRange = TimeRange.GetValue(context);
        _warmUpSteps = WarmUpStepCount.GetValue(context).Clamp(0, 1000);
        var res=Resolution.GetValue(context);
        _defaultResolution = new Int2(res.X.Clamp(1, 16383), 
                                      res.Y.Clamp(1, 16383));
    }

    private bool  TryUpdateImage(EvaluationContext context, int index, out float usedTime, out Texture2D image)
    {
        ResourceManager.Device.ImmediateContext.Flush();
         
        usedTime = 0;
        var previousKeyframeTime = context.LocalTime;
        var previousEffectTime = context.LocalFxTime;
        var previousResolution = context.RequestedResolution;
        var previousGizmo = context.ShowGizmos;
        context.ShowGizmos = GizmoVisibility.Off;

        var f = _stepCount <= 1 ? 0.5f : (float)index / _stepCount;
        Playback.Current.IsRenderingToFile = true;
        var stepStep = MathUtils.Lerp(_timeRange.X, _timeRange.Y, f);
        image = null;
        for (int midStepIndex = 0; midStepIndex <= _warmUpSteps; midStepIndex++)
        {
            var subTime = _warmUpSteps <= 1 ? 0 : (float)midStepIndex / _warmUpSteps;
            var time = stepStep + subTime;
            usedTime = time;
            
            context.LocalTime = time;
            context.LocalFxTime = time;
            context.RequestedResolution = _defaultResolution;

            DirtyFlag.InvalidationRefFrame++;
            Image.Invalidate();
            Image.DirtyFlag.ForceInvalidate();

            image = Image.GetValue(context);
        }
        
        context.ShowGizmos = previousGizmo;
        context.LocalTime = previousKeyframeTime;
        context.LocalFxTime = previousEffectTime;
        context.RequestedResolution = previousResolution;
        return !Playback.OpNotReady;
    }

    private enum States
    {
        Waiting,
        UpdatingReferences,
        Testing,
        Completed,
        Unknown,
    }

    
    private void UpdateAndSaveReferences(EvaluationContext context, List<string> testResultBuilder)
    {
        for (; _testIndex < _stepCount; _testIndex++)
        {
            var ready= TryUpdateImage(context, _testIndex, out _, out var image);
            if (!ready)
            {
                Log.Debug(" waiting for op to be ready...", this);    
                return;
            }
            var filepath = GetReferenceFilepath(_testIndex);
            Log.Debug("Saving image " + filepath, this);

            SaveTexture(image, filepath);
            testResultBuilder.Add("saved " + filepath);
        }

        _state = States.Completed;
        Playback.Current.IsRenderingToFile = false;
    }
    

    private void ConductTests(EvaluationContext context, List<string> testResult)
    {
        SharpDX.Direct3D11.Texture2D diffColorImage = null;
        for (; _testIndex < _stepCount; _testIndex++)
        {
            var referenceFilepath = GetReferenceFilepath(_testIndex);
            var testName = GetTestName(_testIndex);
            
            if (!TryLoadTextureFromFile(ResourceManager.Device, referenceFilepath, out var referenceImage))
            {
                Log.Warning($"Can't find image... {referenceFilepath}");
                continue;
            }

            var ready = TryUpdateImage(context, _testIndex, out var time, out var image);
            if (!ready)
            {
                Log.Debug(" waiting for op to be ready...", this);    
                return;
            }

            if (image == null)
            {
                Log.Debug(" failed to get image...", this);    
                return;
            }
            
            var currentWithCpuAccess = _textureBgraReadAccess.ConvertToCpuReadableBgra(image);
            var deviation = CompareImage(currentWithCpuAccess, referenceImage);
            var failPath = GetReferenceFilepath(_testIndex, "FAIL");
            
            var timeLabel = time == 0 ? string.Empty : $"@{time:0.00}";
            if (deviation < _threshold)
            {
                if (File.Exists(failPath))
                {
                    File.Delete(failPath);
                }

                testResult.Add($"{testName} {timeLabel}: PASSED");
            }
            else
            {
                SaveTexture(image, GetReferenceFilepath(_testIndex, "FAIL"));
                testResult.Add($"{testName} {timeLabel}: FAILED ({deviation:0.00} > {_threshold})");
            }
            
            Utilities.Dispose(ref diffColorImage);
        }
        _state = States.Completed;
        Playback.Current.IsRenderingToFile = false;
    }

    private static void SaveTexture(Texture2D texture, string filePath)
    {
        _textureBgraReadAccess.InitiateConvertAndReadBack(texture, WriteTextureToFile, filePath);
    }

    private string GetTestName(int index)
    {
        List<string> parts = [];
        
        parts.Add(GetCompositionName());

        if (!string.IsNullOrEmpty(SymbolChild.Name))
        {
            parts.Add(SymbolChild.Name);
        }
        else
        {
            parts.Add("Test_" + SymbolChildId.ShortenGuid(5));
        }
        
        parts.Add($"{index:00}");
        
        return string.Join(" / ", parts);
    }

    private string GetReferenceFilepath(int index, string suffix =null)
    {
        List<string> parts = [];

        if (!string.IsNullOrEmpty(SymbolChild.Name))
        {
            parts.Add(Regex.Replace(SymbolChild.Name, @"[^a-zA-Z0-9_]", "_"));
        }

        parts.Add(SymbolChildId.ShortenGuid(5));

        parts.Add($"{index:00}");
        
        if(!string.IsNullOrEmpty(suffix))
            parts.Add(suffix);
        
        var baseName = string.Join('_', parts);
        baseName += ".png";

        return Path.Join( FileLocations.TestReferencesFolder, GetCompositionName(), baseName);
    }

    private string GetCompositionName()
    {
        var compositionName = !string.IsNullOrEmpty(Parent?.Symbol.Name) 
                                  ? Parent.Symbol.Name 
                                  : "Misc";
        return compositionName;
    }

    private static bool TryLoadTextureFromFile(SharpDX.Direct3D11.Device device, string filePath, out SharpDX.Direct3D11.Texture2D image)
    {
        if (!File.Exists(filePath))
        {
            image = null;
            return false;
        }

        var imagingFactory = new ImagingFactory();

        // Decode the image
        var decoder = new BitmapDecoder(imagingFactory, filePath, DecodeOptions.CacheOnDemand);
        var frame = decoder.GetFrame(0);

        // Convert to 32bpp RGBA
        var converter = new FormatConverter(imagingFactory);
        converter.Initialize(frame, PixelFormat.Format32bppRGBA);

        int width = converter.Size.Width;
        int height = converter.Size.Height;

        // Copy pixels into a buffer
        var stride = width * 4;
        var buffer = new DataStream(height * stride, true, true);
        converter.CopyPixels(stride, buffer);

        // Create texture description
        var texDesc = new Texture2DDescription
                          {
                              Width = width,
                              Height = height,
                              ArraySize = 1,
                              BindFlags = BindFlags.ShaderResource,
                              Usage = ResourceUsage.Immutable,
                              CpuAccessFlags = CpuAccessFlags.None,
                              Format = Format.R8G8B8A8_UNorm,
                              MipLevels = 1,
                              OptionFlags = ResourceOptionFlags.None,
                              SampleDescription = new SampleDescription(1, 0)
                          };

        // Define initial data
        var dataBox = new DataBox(buffer.DataPointer, stride, 0);
        var dataBoxes = new[] { dataBox };
        var texture = new SharpDX.Direct3D11.Texture2D(device, texDesc, dataBoxes);

        // Cleanup
        buffer.Dispose();
        converter.Dispose();
        frame.Dispose();
        decoder.Dispose();
        imagingFactory.Dispose();
        image = texture;
        return true;
    }

    /// <remarks>
    /// Sadly, we have to deal with BRGA vs RGBA because we convert the current image to brga on the GPU
    /// to speedup writing as PNG.  
    /// </remarks>
    private float CompareImage(SharpDX.Direct3D11.Texture2D currentBgraWithCpuAccess,
                               SharpDX.Direct3D11.Texture2D reference)
    {
        try
        {
            if (currentBgraWithCpuAccess == null || reference == null ||
                currentBgraWithCpuAccess.Description.Width != reference.Description.Width ||
                currentBgraWithCpuAccess.Description.Height != reference.Description.Height)
            {
                Log.Warning($"{GetReferenceFilepath(0)} incorrect size? {currentBgraWithCpuAccess?.Description.Width}x{currentBgraWithCpuAccess?.Description.Height} vs {reference?.Description.Width}×{reference?.Description.Height}");
                return -1f;
            }

            if (currentBgraWithCpuAccess.Description.Format != Format.B8G8R8A8_UNorm)
            {
                Log.Warning($"Source image format {currentBgraWithCpuAccess.Description.Format} is expected to be BGRA8");
                return -1f;
            }
            
            
            var immediateContext = ResourceManager.Device.ImmediateContext;
            var referenceDesc = new Texture2DDescription()
                                    {
                                        BindFlags = BindFlags.None,
                                        Format = reference.Description.Format,
                                        Width = reference.Description.Width,
                                        Height = reference.Description.Height,
                                        MipLevels = reference.Description.MipLevels,
                                        SampleDescription = new SampleDescription(1, 0),
                                        Usage = ResourceUsage.Staging,
                                        OptionFlags = ResourceOptionFlags.None,
                                        CpuAccessFlags = CpuAccessFlags.Read,
                                        ArraySize = 1
                                    };
            var referenceWithCpuAccess = new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, referenceDesc);
            immediateContext.CopyResource(reference, referenceWithCpuAccess);
            ResourceManager.Device.ImmediateContext.Flush();  // Ensure the copy is complete
            

            var currentDataBox =
                immediateContext.MapSubresource(currentBgraWithCpuAccess, 0, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var currentStream);
            currentStream.Position = 0;

            var refDataBox =
                immediateContext.MapSubresource(referenceWithCpuAccess, 0, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var referenceStream);
            referenceStream.Position = 0;


            double deviation = 0;
            for (int y = 0; y < currentBgraWithCpuAccess.Description.Height; ++y)
            {
                for (int x = 0; x < currentBgraWithCpuAccess.Description.Width; ++x)
                {
                    var currentBgra = new Color4(currentStream.Read<Int32>());
                    var referenceRgba = new Color4(referenceStream.Read<Int32>());
                    deviation += Math.Abs(currentBgra.Alpha - referenceRgba.Alpha)
                                 + Math.Abs(currentBgra.Blue - referenceRgba.Red)
                                 + Math.Abs(currentBgra.Green - referenceRgba.Green)
                                 + Math.Abs(currentBgra.Red - referenceRgba.Blue);
                }

                currentStream.Position += currentDataBox.RowPitch - currentBgraWithCpuAccess.Description.Width * 4;
                referenceStream.Position += refDataBox.RowPitch - currentBgraWithCpuAccess.Description.Width * 4;
            }

            deviation /= currentBgraWithCpuAccess.Description.Width * currentBgraWithCpuAccess.Description.Height;

            immediateContext.UnmapSubresource(currentBgraWithCpuAccess, 0);
            Utilities.Dispose(ref currentStream);
            immediateContext.UnmapSubresource(referenceWithCpuAccess, 0);
            Utilities.Dispose(ref referenceStream);
            Utilities.Dispose(ref referenceWithCpuAccess);
            return (float)deviation;
        }
        catch (Exception ex)
        {
            Log.Error(" Failed to compare " + ex.Message, this);
            return -1;
        }
    }

    
    private static void WriteTextureToFile(TextureBgraReadAccess.ReadRequestItem request)
    {
        var requestCpuAccessTexture = request.CpuAccessTexture;
        var requestFilepath = request.Filepath;

        var immediateContext = ResourceManager.Device.ImmediateContext;
        if (requestCpuAccessTexture.IsDisposed)
        {
            Log.Debug("ScreenshotWriter: Texture was disposed before readback was complete");
            return;
        }
        
        // Make sure target folder exists
        if (string.IsNullOrEmpty(requestFilepath)) 
        {
            Log.Debug("ScreenshotWriter: Target folder missing?");
            return;
        }

        var folder = Path.GetDirectoryName(requestFilepath);
        if (!string.IsNullOrEmpty(folder))
        {
            //Log.Debug($"Creating {folder}...");
            Directory.CreateDirectory(folder);
        }

        var dataBox = immediateContext.MapSubresource(requestCpuAccessTexture,
                                                      0,
                                                      0,
                                                      SharpDX.Direct3D11.MapMode.Read,
                                                      SharpDX.Direct3D11.MapFlags.None,
                                                      out var imageStream);
        using var dataStream = imageStream;

        var width = requestCpuAccessTexture.Description.Width;
        var height = requestCpuAccessTexture.Description.Height;
        var factory = new ImagingFactory();

        WICStream stream;
        try
        {
            stream = new WICStream(factory, requestFilepath, NativeFileAccess.Write);
        }
        catch (Exception e)
        {
            Log.Warning("Failed to export image: " + e.Message);
            return;
        }

        BitmapEncoder encoder = new PngBitmapEncoder(factory);
        encoder.Initialize(stream);

        // Create a Frame encoder
        var bitmapFrameEncode = new BitmapFrameEncode(encoder);
        bitmapFrameEncode.Initialize();
        bitmapFrameEncode.SetSize(width, height);
        var formatId = PixelFormat.Format32bppRGBA;
        bitmapFrameEncode.SetPixelFormat(ref formatId);

        var rowStride = PixelFormat.GetStride(formatId, width);
        var outBufferSize = height * rowStride;
        var outDataStream = new DataStream(outBufferSize, true, true);

        try
        {
            // Note: dataBox.RowPitch and outputStream.RowPitch can diverge if width is not divisible by 16.
            for (var loopY = 0; loopY < height; loopY++)
            {
                imageStream.Position = (long)(loopY) * dataBox.RowPitch;
                outDataStream.WriteRange(imageStream.ReadRange<byte>(rowStride));
            }

            // Copy the BGRA pixels from the buffer to the Wic Bitmap Frame encoder
            bitmapFrameEncode.WritePixels(height, new DataRectangle(outDataStream.DataPointer, rowStride));

            // Commit changes
            bitmapFrameEncode.Commit();
            encoder.Commit();
        }
        catch (Exception e)
        {
            Log.Error($"Screenshot internal image copy failed : {e.Message}");
        }
        finally
        {
            imageStream.Dispose();
            outDataStream.Dispose();
            bitmapFrameEncode.Dispose();
            encoder.Dispose();
            stream.Dispose();
        }
    }
    
    private States _state = States.Waiting;
    private int _testIndex;

    private int _warmUpSteps;
    private int _stepCount;
    private float _threshold;
    private Vector2 _timeRange;
    private bool _complainedOnce;

    // private const string ReferencesSubFolder = "tests";
    // private const string ReferencesSourceFolder = ".tixl";
    //
    // #if DEBUG
    // private const string ReferencesTargetFolder = FileLocations.AppSubFolder;
    // #else
    // private const string ReferencesTargetFolder = ".tixl";
    // #endif

    // private const int DefaultHeight = 120;
    // private const int DefaultWidth = (int)(DefaultHeight * (16f / 9));
    private  Int2 _defaultResolution;
    

    private const string TestFrameKey = "_TestFrame";
    private const string TestResultKey = "_TestResult";
    private const string TestActionKey = "_TestAction";

    private static readonly TextureBgraReadAccess _textureBgraReadAccess = new(true);

        [Input(Guid = "ed9887ca-5ee4-4fb7-a835-071de255a893")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "9628A5C6-E731-4DF7-B49B-E617C75CFDA4")]
        public readonly InputSlot<float> Threshold = new InputSlot<float>();

        [Input(Guid = "59D30AB3-99D5-441A-A6E7-EB76278F6AC1")]
        public readonly InputSlot<System.Numerics.Vector2> TimeRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "27ADB1E1-0704-4F1F-894D-BF38E3C8D982")]
        public readonly InputSlot<int> StepCount = new InputSlot<int>();

        [Input(Guid = "ADFE6FE4-7FA7-4F1C-9D66-1CBF9B383BA1")]
        public readonly InputSlot<int> WarmUpStepCount = new InputSlot<int>();

        [Input(Guid = "085CA93B-4167-444C-B3E0-A0628E93C633")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

}