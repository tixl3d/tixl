#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using ImGuiNET;
using MsdfAtlasGen;
using Msdfgen;
using Msdfgen.Extensions;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Utilities
{
    public static class MsdfGeneration
    {
        private static bool _useRecommended = true;

        private enum ColoringStrategy
        {
            Simple,
            InkTrap
        }

        private enum ErrorCorrectionMode
        {
            Disabled,
            Indiscriminate,
            EdgeOnly,
            Auto
        }

        public enum CharsetMode
        {
            Ascii,
            ExtendedAscii,
            Custom,
            AllGlyphs
        }

        #region Default Values
        private const float DefaultFontSize = 30;
        private const int DefaultWidth = 1024;
        private const int DefaultHeight = 1024;
        private const float DefaultMiterLimit = 3.0f;
        private const int DefaultSpacing = 2;
        private const float DefaultRange = 7.0f;
        private const float DefaultAngleThreshold = 3.0f;
        private const ColoringStrategy DefaultExampleColoring = ColoringStrategy.Simple; // As per recommended settings
        private const ErrorCorrectionMode DefaultErrorCorrection = ErrorCorrectionMode.Indiscriminate;
        private const bool DefaultOverlap = true;
        private static readonly Vector4 DefaultPadding = Vector4.Zero;
        #endregion

        // Settings fields
        private static ColoringStrategy _coloringStrategy = DefaultExampleColoring;
        private static ErrorCorrectionMode _errorCorrection = DefaultErrorCorrection;
        private static CharsetMode _charsetMode = CharsetMode.Ascii;
        private static bool _overlap = DefaultOverlap;
        private static Vector4 _outerPadding = DefaultPadding;
        private static float _fontSize = DefaultFontSize;
        private static int _width = DefaultWidth;
        private static int _height = DefaultHeight;
        private static float _miterLimit = DefaultMiterLimit;
        private static int _spacing = DefaultSpacing;
        private static float _rangeValue = DefaultRange;
        private static float _angleThreshold = DefaultAngleThreshold;

        private static string? _customCharsetPath = null;
        private static string? _fontFilePath = null;
        private static SymbolPackage? _selectedPackage;

        // Status & Progress
        private static bool _isGenerating;
        private static float _generationProgress;
        private static string _progressText = "";
        private static string _statusMessage = "";
        private static bool _isStatusError = false;
        private static string _lastOutputDir = "";

        // Metrics
        private class GenerationMetrics
        {
            public TimeSpan LoadTime;
            public TimeSpan CharsetTime;
            public TimeSpan ColoringTime;
            public TimeSpan PackTime;
            public TimeSpan GenerateTime;
            public TimeSpan SaveTime;
            public TimeSpan TotalTime;
        }

        private static GenerationMetrics? _lastMetrics;

        public static void Draw()
        {
            FormInputs.SetIndent(50 * T3Ui.UiScaleFactor);

            FormInputs.AddSectionHeader("MSDF Generation");
            CustomComponents.HelpText("Generate MSDF fonts from .ttf/.otf files using Msdfgen.\nChecks for 'Resources/fonts'.");
            FormInputs.AddVerticalSpace();

            if (_isGenerating)
            {
                ImGui.BeginDisabled();
            }

            // Font File
            FormInputs.AddFilePicker("Font File", ref _fontFilePath, null, null, "Select .ttf/.otf file", FileOperations.FilePickerTypes.File);

            // Recommended Toggle
            FormInputs.AddCheckBox("Use Recommended Settings", ref _useRecommended);

            if (!_useRecommended)
            {
                FormInputs.SetIndent(100 * T3Ui.UiScaleFactor);

                // Size
                DrawSettingWithReset("Size", ref _fontSize, DefaultFontSize, 1, 500, 1);

                // Dimensions
                DrawSettingWithReset("Width", ref _width, DefaultWidth, 128, 8192, 128);
                DrawSettingWithReset("Height", ref _height, DefaultHeight, 128, 8192, 128);

                // Generator Params
                DrawSettingWithReset("Miter Limit", ref _miterLimit, DefaultMiterLimit, 0, 10, 0.1f);
                DrawSettingWithReset("Spacing", ref _spacing, DefaultSpacing, 0, 32, 1);
                DrawSettingWithReset("Range", ref _rangeValue, DefaultRange, 0.1f, 20, 0.1f);
                DrawSettingWithReset("Angle Thres.", ref _angleThreshold, DefaultAngleThreshold, 0, 6, 0.1f);

                // Enums and Bools
                DrawEnumWithReset(ref _coloringStrategy, "Coloring Config.", DefaultExampleColoring);
                DrawEnumWithReset(ref _errorCorrection, "Error Correction", DefaultErrorCorrection);
                DrawBoolWithReset("Overlap Support", ref _overlap, DefaultOverlap);

                // Charset
                FormInputs.AddEnumDropdown(ref _charsetMode, "Charset");
                if (_charsetMode == CharsetMode.Custom)
                {
                    FormInputs.AddFilePicker("Charset File", ref _customCharsetPath, null, null, "Select .txt file", FileOperations.FilePickerTypes.File);
                }

                // Padding
                FormInputs.DrawFieldSetHeader("Outer Padding");
                var padding = _outerPadding;
                var changed = FormInputs.AddFloat("Top", ref padding.X, 0, 100, 1);
                changed |= FormInputs.AddFloat("Right", ref padding.Y, 0, 100, 1);
                changed |= FormInputs.AddFloat("Bottom", ref padding.Z, 0, 100, 1);
                changed |= FormInputs.AddFloat("Left", ref padding.W, 0, 100, 1);
                if (changed) _outerPadding = padding;

                // Reset Padding Button
                if (_outerPadding != Vector4.Zero)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Reset##Padding")) _outerPadding = Vector4.Zero;
                }

                FormInputs.ApplyIndent();
            }

            FormInputs.AddVerticalSpace();
            FormInputs.SetIndent(90 * T3Ui.UiScaleFactor);

            // Project Selection
            DrawProjectSelection(out var usagePackage);

            // Generate Button
            bool hasFile = !string.IsNullOrEmpty(_fontFilePath) && File.Exists(_fontFilePath);
            bool hasCharset = _charsetMode != CharsetMode.Custom || (!string.IsNullOrEmpty(_customCharsetPath) && File.Exists(_customCharsetPath));

            if (CustomComponents.DisablableButton("Generate MSDF", hasFile && hasCharset && usagePackage != null && !_isGenerating))
            {
                GenerateAsync(usagePackage!).ContinueWith(t =>
                                                          {
                                                              if (t.Exception != null)
                                                              {
                                                                  Log.Error("Unhandled exception in MSDF generation: " + t.Exception);
                                                                  _statusMessage = $"Error: {t.Exception.Message}";
                                                                  _isStatusError = true;
                                                                  _isGenerating = false;
                                                              }
                                                          }, TaskContinuationOptions.OnlyOnFaulted);
            }

            if (_isGenerating)
            {
                ImGui.EndDisabled();
            }

            // Validations
            if (!hasFile) CustomComponents.TooltipForLastItem("Please select a valid .ttf/.otf font file.");
            else if (!hasCharset) CustomComponents.TooltipForLastItem("Please select a valid charset .txt file.");
            else if (usagePackage == null) CustomComponents.TooltipForLastItem("Please select a target project.");

            // Progress Bar
            if (_isGenerating)
            {
                FormInputs.AddVerticalSpace();
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UiColors.StatusAutomated.Rgba);
                ImGui.ProgressBar(_generationProgress, new System.Numerics.Vector2(-1, 4), "");
                ImGui.PopStyleColor();
                ImGui.Text(_progressText);
                FormInputs.AddVerticalSpace();
            }

            // Status and Results
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                var color = _isStatusError ? UiColors.StatusError : UiColors.StatusAutomated;
                ImGui.TextColored(color, _statusMessage);

                // Performance Tooltip on Success
                if (!_isStatusError && _lastMetrics != null && ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Performance Metrics:");
                    ImGui.Text($"Load Font: {_lastMetrics.LoadTime.TotalSeconds:F3}s");
                    ImGui.Text($"Charset:   {_lastMetrics.CharsetTime.TotalSeconds:F3}s");
                    ImGui.Text($"Coloring:  {_lastMetrics.ColoringTime.TotalSeconds:F3}s");
                    ImGui.Text($"Packing:   {_lastMetrics.PackTime.TotalSeconds:F3}s");
                    ImGui.Text($"Generate:  {_lastMetrics.GenerateTime.TotalSeconds:F3}s");
                    ImGui.Text($"Saving:    {_lastMetrics.SaveTime.TotalSeconds:F3}s");
                    ImGui.Separator();
                    ImGui.Text($"Total:     {_lastMetrics.TotalTime.TotalSeconds:F3}s");
                    ImGui.EndTooltip();
                }

                if (!_isStatusError && !_isGenerating && !string.IsNullOrEmpty(_lastOutputDir))
                {
                    if (ImGui.Button("Open Output Folder"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(_lastOutputDir);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Click to open the folder containing the generated assets.");
                    }
                }
            }
        }

        private static void DrawProjectSelection(out SymbolPackage? usagePackage)
        {
            var internalPackage = GetPackageContainingPath(_fontFilePath);
            usagePackage = null;

            if (internalPackage != null)
            {
                var name = internalPackage.DisplayName;
                ImGui.BeginDisabled();
                FormInputs.AddStringInput("Target Project", ref name);
                ImGui.EndDisabled();
                usagePackage = internalPackage;
            }
            else
            {
                var editablePackages = EditableSymbolProject.AllProjects.ToList();
                if (_selectedPackage == null || !editablePackages.Contains(_selectedPackage))
                {
                    var focusedProject = ProjectView.Focused?.OpenedProject.Package;
                    _selectedPackage = focusedProject != null && !focusedProject.IsReadOnly
                                           ? focusedProject
                                           : editablePackages.FirstOrDefault();
                }

                var packageNames = editablePackages.Select(p => p.DisplayName).OrderBy(n => n);
                var selectedName = _selectedPackage?.DisplayName ?? "";

                if (FormInputs.AddStringInputWithSuggestions("Target Project", ref selectedName, packageNames, "Select Project"))
                {
                    _selectedPackage = editablePackages.FirstOrDefault(p => p.DisplayName == selectedName);
                }

                if (_selectedPackage == null || _selectedPackage.DisplayName != selectedName)
                {
                    var match = editablePackages.FirstOrDefault(p => p.DisplayName == selectedName);
                    if (match != null) _selectedPackage = match;
                }

                usagePackage = _selectedPackage;
                if (usagePackage == null)
                {
                    ImGui.Indent(150 * T3Ui.UiScaleFactor);
                    ImGui.TextColored(UiColors.StatusError, "Invalid or No Project selected.");
                    ImGui.Unindent(150 * T3Ui.UiScaleFactor);
                }
            }
        }

        private static void DrawSettingWithReset<T>(string label, ref T value, T defaultValue, float min, float max, float step = 1)
            where T : struct, IConvertible
        {
            if (typeof(T) == typeof(int))
            {
                int val = Convert.ToInt32(value);
                if (FormInputs.AddInt(label, ref val, (int)min, (int)max, (int)step)) value = (T)Convert.ChangeType(val, typeof(T));
            }
            else if (typeof(T) == typeof(float))
            {
                float val = Convert.ToSingle(value);
                if (FormInputs.AddFloat(label, ref val, min, max, step)) value = (T)Convert.ChangeType(val, typeof(T));
            }

            if (!EqualityComparer<T>.Default.Equals(value, defaultValue))
            {
                ImGui.SameLine();
                ImGui.PushID(label);
                if (CustomComponents.IconButton(Icon.Revert, new System.Numerics.Vector2(ImGui.GetFrameHeight())))
                {
                    value = defaultValue;
                }

                ImGui.PopID();
            }
        }

        private static void DrawBoolWithReset(string label, ref bool value, bool defaultValue)
        {
            FormInputs.AddCheckBox(label, ref value);
            if (value != defaultValue)
            {
                ImGui.SameLine();
                ImGui.PushID(label);
                if (CustomComponents.IconButton(Icon.Revert, new System.Numerics.Vector2(ImGui.GetFrameHeight())))
                {
                    value = defaultValue;
                }

                ImGui.PopID();
            }
        }

        private static void DrawEnumWithReset<T>(ref T value, string label, T defaultValue) where T : struct, Enum
        {
            FormInputs.AddEnumDropdown(ref value, label);
            if (!EqualityComparer<T>.Default.Equals(value, defaultValue))
            {
                ImGui.SameLine();
                ImGui.PushID(label);
                if (CustomComponents.IconButton(Icon.Revert, new System.Numerics.Vector2(ImGui.GetFrameHeight())))
                {
                    value = defaultValue;
                }

                ImGui.PopID();
            }
        }

        public static void RefreshSelection()
        {
            _selectedPackage = null;
        }

        private record GenerationResult(
            bool Success,
            string Message = "",
            GenerationMetrics? Metrics = null,
            string OutputDir = "");

        private static async Task GenerateAsync(SymbolPackage package)
        {
            // Initialize UI State
            _statusMessage = "";
            _isStatusError = false;
            _isGenerating = true;
            _generationProgress = 0;
            _progressText = "Starting generation...";
            _lastMetrics = new GenerationMetrics();

            // 1. Pre-flight check (Avoids starting a Task if basic requirements aren't met)
            if (string.IsNullOrEmpty(_fontFilePath) || !File.Exists(_fontFilePath))
            {
                _statusMessage = $"Error: Font file not found at {_fontFilePath}";
                _isStatusError = true;
                _isGenerating = false;
                return;
            }

            var progress = new Progress<GeneratorProgress>(p =>
                                                           {
                                                               _generationProgress = (float)p.Proportion;
                                                               _progressText = $"Generating: {p.Current}/{p.Total} ({p.GlyphName})";
                                                           });

            // 2. Run logic on background thread
            var result = await Task.Run(() => PerformGeneration(package, progress));

            // 3. Handle result
            if (result.Success)
            {
                _lastMetrics = result.Metrics;
                _lastOutputDir = result.OutputDir;
                _statusMessage = $"Success! ({_lastMetrics?.TotalTime.TotalSeconds:F2}s)";
                Log.Debug($"MSDF Generation successful!");
            }
            else
            {
                _statusMessage = $"Error: {result.Message}";
                _isStatusError = true;
                Log.Error($"MSDF Generation Failed: {result.Message}");
            }

            _isGenerating = false;
            _progressText = "";
        }

        private static GenerationResult PerformGeneration(SymbolPackage package, IProgress<GeneratorProgress> progress)
        {
            var metrics = new GenerationMetrics();
            var sw = Stopwatch.StartNew();
            var settings = GetSettings();
            var outputDir = PrepareOutputDirectory(package);
            var fontName = Path.GetFileNameWithoutExtension(settings.FontPath);

            var imageOut = Path.Combine(outputDir, $"{fontName}_msdf.png");
            var fontOut = Path.Combine(outputDir, $"{fontName}_msdf.fnt");

            // 1. Initialize & Load
            using var ft = FreetypeHandle.Initialize();
            
            if (ft is null)
                return new GenerationResult(false, "Failed to initialize FreeType.");

            using var fontHandle = FontHandle.LoadFont(ft, settings.FontPath);
            if (fontHandle is null)
                return new GenerationResult(false, "Failed to load font.");

            metrics.LoadTime = sw.Elapsed;
            sw.Restart();

            // 2. Charset
            var charset = LoadCharset(settings, fontHandle);
            var fontGeometry = new FontGeometry();
            fontGeometry.LoadCharset(fontHandle, settings.FontSize, charset);
            fontGeometry.SetName(fontName);
            metrics.CharsetTime = sw.Elapsed;
            sw.Restart();

            // 3. Coloring
            var glyphs = fontGeometry.GetGlyphs().Glyphs.ToArray();
            var strategy = settings.Strategy == ColoringStrategy.Simple
                               ? Msdfgen.EdgeColoring.EdgeColoringSimple
                               : (EdgeColoringDelegate)Msdfgen.EdgeColoring.EdgeColoringInkTrap;

            foreach (var glyph in glyphs)
            {
                glyph.EdgeColoring(strategy, settings.AngleThreshold, 0);
            }

            metrics.ColoringTime = sw.Elapsed;
            sw.Restart();

            // 4. Packing
            if (!TryPackGlyphs(glyphs, settings, out var finalW, out var finalH, out var packerScale))
            {
                return new GenerationResult(false, $"Packing {glyphs.Length} glyphs failed. Try increasing resolution.");
            }

            metrics.PackTime = sw.Elapsed;
            sw.Restart();

            // 5. Generation
            var generator = GenerateAtlas(glyphs, finalW, finalH, settings, progress);
            metrics.GenerateTime = sw.Elapsed;
            sw.Restart();

            // 6. Saving
            SaveResults(generator, fontGeometry, settings, imageOut, fontOut, finalW, finalH, packerScale);
            metrics.SaveTime = sw.Elapsed;

            metrics.TotalTime = metrics.LoadTime + metrics.CharsetTime + metrics.ColoringTime + metrics.PackTime + metrics.GenerateTime + metrics.SaveTime;

            return new GenerationResult(true, Metrics: metrics, OutputDir: outputDir);
        }

        private static Charset LoadCharset(GenerationSettings settings, FontHandle fontHandle)
        {
            if (settings.CharsetMode == CharsetMode.AllGlyphs)
            {
                var charset = new Charset();
                if (FontLoader.GetAvailableCodepoints(out var codepoints, fontHandle))
                {
                    foreach (var cp in codepoints) charset.Add(cp);
                }

                return charset;
            }

            if (settings.CharsetMode == CharsetMode.ExtendedAscii) return Charset.EASCII;

            if (settings.CharsetMode == CharsetMode.Custom && File.Exists(settings.CustomCharsetPath))
            {
                var charset = new Charset();
                var text = File.ReadAllText(settings.CustomCharsetPath);
                foreach (var c in text)
                    if (!char.IsControl(c))
                        charset.Add(c);
                return charset;
            }

            return Charset.ASCII;
        }

        private static GenerationSettings GetSettings()
        {
            // If recommended, enforce specific defaults but maybe keep some flexibility if desired? 
            // The request implies recommended should just be standard defaults.
            if (_useRecommended)
            {
                return new GenerationSettings
                           {
                               FontPath = _fontFilePath ?? string.Empty,
                               FontSize = 90.0, // Original recommended value
                               Width = 1024,
                               Height = 1024,
                               MiterLimit = 3.0,
                               Spacing = 2,
                               RangeValue = 2.0,
                               AngleThreshold = 3.0,
                               Strategy = ColoringStrategy.Simple,
                               ErrorCorrection = ErrorCorrectionMode.Indiscriminate,
                               Overlap = true,
                               OuterPadding = new MsdfAtlasGen.Padding(0, 0, 0, 0),
                               CharsetMode = CharsetMode.Ascii
                           };
            }

            return new GenerationSettings
                       {
                           FontPath = _fontFilePath ?? string.Empty,
                           FontSize = _fontSize,
                           Width = _width,
                           Height = _height,
                           MiterLimit = _miterLimit,
                           Spacing = _spacing,
                           RangeValue = _rangeValue,
                           AngleThreshold = _angleThreshold,
                           Strategy = _coloringStrategy,
                           ErrorCorrection = _errorCorrection,
                           Overlap = _overlap,
                           OuterPadding = new MsdfAtlasGen.Padding((int)_outerPadding.W, (int)_outerPadding.Z, (int)_outerPadding.Y, (int)_outerPadding.X),
                           CharsetMode = _charsetMode,
                           CustomCharsetPath = _customCharsetPath ?? string.Empty
                       };
        }

        // ... (TryPackGlyphs, GenerateAtlas, SaveResults, PrepareOutputDirectory, IsPathUnderFolder, GetPackageContainingPath same as before with minor tweaks for new types) 

        private static bool TryPackGlyphs(GlyphGeometry[] glyphs, GenerationSettings settings, out int finalW, out int finalH, out double packerScale)
        {
            var packer = new TightAtlasPacker();
            packer.SetDimensions(settings.Width, settings.Height);
            packer.SetMiterLimit(settings.MiterLimit);
            packer.SetSpacing(settings.Spacing);
            packer.SetPixelRange(new Msdfgen.Range(settings.RangeValue));
            packer.SetOuterPixelPadding(settings.OuterPadding);

            int packResult = packer.Pack(glyphs);
            if (packResult < 0)
            {
                finalW = 0;
                finalH = 0;
                packerScale = 0;
                return false;
            }

            packer.GetDimensions(out finalW, out finalH);
            packerScale = packer.GetScale();
            return true;
        }

        private static ImmediateAtlasGenerator<float> GenerateAtlas(GlyphGeometry[] glyphs, int width, int height, GenerationSettings settings,
                                                                    IProgress<GeneratorProgress> progress)
        {
            var errorMode = settings.ErrorCorrection switch
                                {
                                    ErrorCorrectionMode.Disabled       => ErrorCorrectionConfig.DistanceErrorCorrectionMode.DISABLED,
                                    ErrorCorrectionMode.Indiscriminate => ErrorCorrectionConfig.DistanceErrorCorrectionMode.INDISCRIMINATE,
                                    ErrorCorrectionMode.EdgeOnly       => ErrorCorrectionConfig.DistanceErrorCorrectionMode.EDGE_ONLY,
                                    ErrorCorrectionMode.Auto           => ErrorCorrectionConfig.DistanceErrorCorrectionMode.AUTO,
                                    _                                  => ErrorCorrectionConfig.DistanceErrorCorrectionMode.INDISCRIMINATE
                                };

            var generatorConfig = new MSDFGeneratorConfig(settings.Overlap,
                                                          new ErrorCorrectionConfig(errorMode,
                                                                                    ErrorCorrectionConfig.DistanceCheckMode.CHECK_DISTANCE_ALWAYS));

            // Always 3 channels for MSDF
            var generator = new ImmediateAtlasGenerator<float>(width, height, (bitmap, glyph, attrs) =>
                                                                              {
                                                                                  var proj = glyph.GetBoxProjection();
                                                                                  var gRange = glyph.GetBoxRange();
                                                                                  MsdfGenerator.GenerateMSDF(bitmap, glyph.GetShape()!, proj, gRange,
                                                                                      generatorConfig);
                                                                              }, 3);

            generator.SetThreadCount(Environment.ProcessorCount);
            generator.Generate(glyphs, progress);
            return generator;
        }

        private static void SaveResults(ImmediateAtlasGenerator<float> generator, FontGeometry fontGeometry, GenerationSettings settings, string imageOut,
                                        string fontOut, int finalW, int finalH, double packerScale)
        {
            ImageSaver.Save(generator.AtlasStorage.Bitmap, imageOut);
            var metrics = fontGeometry.GetMetrics();
            FntExporter.Export([fontGeometry], ImageType.Msdf, finalW, finalH, settings.FontSize, settings.RangeValue,
                               Path.GetFileName(imageOut), fontOut, metrics, YAxisOrientation.Upward, settings.OuterPadding, settings.Spacing, packerScale);
        }

        private static string PrepareOutputDirectory(SymbolPackage package)
        {
            var outputDir = Path.Combine(package.AssetsFolder, "fonts");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            return outputDir;
        }

        private static bool IsPathUnderFolder(string fullPath, string? folderPath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(folderPath)) return false;
            try
            {
                fullPath = Path.GetFullPath(fullPath);
                folderPath = Path.GetFullPath(folderPath);
            }
            catch
            {
                return false;
            }

            var sep = Path.DirectorySeparatorChar.ToString();
            if (!folderPath.EndsWith(sep)) folderPath += sep;
            return fullPath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase);
        }

        private static SymbolPackage? GetPackageContainingPath(string? fontPath)
        {
            if (string.IsNullOrWhiteSpace(fontPath)) return null;
            try
            {
                var full = Path.GetFullPath(fontPath);
                return SymbolPackage.AllPackages.FirstOrDefault(p => IsPathUnderFolder(full, p.Folder) || IsPathUnderFolder(full, p.AssetsFolder));
            }
            catch
            {
                return null;
            }
        }

        private sealed record GenerationSettings
        {
            public string FontPath { get; init; } = "";
            public double FontSize { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
            public double MiterLimit { get; init; }
            public int Spacing { get; init; }
            public double RangeValue { get; init; }
            public double AngleThreshold { get; init; }
            public ColoringStrategy Strategy { get; init; }
            public ErrorCorrectionMode ErrorCorrection { get; init; }
            public bool Overlap { get; init; }
            public MsdfAtlasGen.Padding OuterPadding { get; init; } = new(0, 0, 0, 0);
            public CharsetMode CharsetMode { get; init; }
            public string CustomCharsetPath { get; init; } = "";
        }
    }
}