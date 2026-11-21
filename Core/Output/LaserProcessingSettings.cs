#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// The laser pipeline configuration carried by a laser output: the optimizer and safety settings
/// that the output operators apply when they stream for this output — the setup-owned replacement
/// for graph-side ops — it is edited in the Output Setup. Safety starts <see cref="SafetySettings.Calibrated"/>
/// = false: until the venue is aimed, only the manual alignment rectangle streams.
/// </summary>
public sealed class LaserProcessingSettings
{
    public OptimizerSettings Optimizer = new();
    public SafetySettings Safety = new();
    public MotionSettings Motion = new();

    public static LaserProcessingSettings Default => new();

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("Optimizer");
        writer.WriteStartObject();
        writer.WriteValue("EnableOptimization", Optimizer.EnableOptimization);
        writer.WriteValue("MaxJumpDistance", Optimizer.MaxJumpDistance);
        writer.WriteValue("BlankingDelayPoints", Optimizer.BlankingDelayPoints);
        writer.WriteValue("CornerAngleThreshold", Optimizer.CornerAngleThreshold);
        writer.WriteValue("CornerAnchorCount", Optimizer.CornerAnchorCount);
        writer.WriteEndObject();

        writer.WritePropertyName("Motion");
        writer.WriteStartObject();
        writer.WriteValue("DrawingSpeedUs", Motion.DrawingSpeedUs);
        writer.WriteValue("MotionResponseUs", Motion.MotionResponseUs);
        writer.WriteValue("CornerSettleUs", Motion.CornerSettleUs);
        writer.WriteValue("TransitionUs", Motion.TransitionUs);
        writer.WriteEndObject();

        writer.WritePropertyName("Safety");
        writer.WriteStartObject();
        writer.WriteValue("Calibrated", Safety.Calibrated);
        writer.WriteValue("BeamEnable", Safety.BeamEnable);
        writer.WriteValue("SafeZoneCenterX", Safety.SafeZoneCenterX);
        writer.WriteValue("SafeZoneCenterY", Safety.SafeZoneCenterY);
        writer.WriteValue("SafeZoneWidth", Safety.SafeZoneWidth);
        writer.WriteValue("SafeZoneHeight", Safety.SafeZoneHeight);
        writer.WriteValue("MaxStepPerPoint", Safety.MaxStepPerPoint);
        writer.WriteValue("MaxStaticPoints", Safety.MaxStaticPoints);
        writer.WriteValue("MaxBrightness", Safety.MaxBrightness);
        writer.WriteValue("ColorGainR", Safety.ColorGainR);
        writer.WriteValue("ColorGainG", Safety.ColorGainG);
        writer.WriteValue("ColorGainB", Safety.ColorGainB);
        writer.WriteValue("ColorMinR", Safety.ColorMinR);
        writer.WriteValue("ColorMinG", Safety.ColorMinG);
        writer.WriteValue("ColorMinB", Safety.ColorMinB);
        writer.WriteValue("ColorGammaR", Safety.ColorGammaR);
        writer.WriteValue("ColorGammaG", Safety.ColorGammaG);
        writer.WriteValue("ColorGammaB", Safety.ColorGammaB);
        writer.WriteValue("TtlMode", Safety.TtlMode);
        writer.WriteValue("MaxStaticIntensity", Safety.MaxStaticIntensity);
        writer.WriteValue("FlipX", Safety.FlipX);
        writer.WriteValue("FlipY", Safety.FlipY);
        writer.WriteValue("ColorShiftR", Safety.ColorShiftR);
        writer.WriteValue("ColorShiftG", Safety.ColorShiftG);
        writer.WriteValue("ColorShiftB", Safety.ColorShiftB);
        writer.WriteValue("RenderSafetyShapes", Safety.RenderSafetyShapes);
        writer.WriteValue("RenderShapesLevel", Safety.RenderShapesLevel);
        writer.WriteValue("RenderShapesUseCustomColor", Safety.RenderShapesUseCustomColor);
        writer.WriteValue("RenderShapesColorR", Safety.RenderShapesColorR);
        writer.WriteValue("RenderShapesColorG", Safety.RenderShapesColorG);
        writer.WriteValue("RenderShapesColorB", Safety.RenderShapesColorB);
        writer.WriteValue("DesiredFps", Safety.DesiredFps);
        writer.WriteValue("SetupCenterX", Safety.SetupCenterX);
        writer.WriteValue("SetupCenterY", Safety.SetupCenterY);
        writer.WriteValue("SetupWidth", Safety.SetupWidth);
        writer.WriteValue("SetupHeight", Safety.SetupHeight);
        writer.WriteValue("FrameWarp", Safety.FrameWarp);
        writer.WriteValue("WarpCorner1X", Safety.WarpCorner1X);
        writer.WriteValue("WarpCorner1Y", Safety.WarpCorner1Y);
        writer.WriteValue("WarpCorner2X", Safety.WarpCorner2X);
        writer.WriteValue("WarpCorner2Y", Safety.WarpCorner2Y);
        writer.WriteValue("WarpCorner3X", Safety.WarpCorner3X);
        writer.WriteValue("WarpCorner3Y", Safety.WarpCorner3Y);
        writer.WriteValue("WarpCorner4X", Safety.WarpCorner4X);
        writer.WriteValue("WarpCorner4Y", Safety.WarpCorner4Y);
        if (Safety.Masks.Count > 0)
        {
            writer.WritePropertyName("Masks");
            writer.WriteStartArray();
            foreach (var mask in Safety.Masks)
            {
                writer.WriteStartObject();
                writer.WriteValue("X1", mask.X1);
                writer.WriteValue("Y1", mask.Y1);
                writer.WriteValue("X2", mask.X2);
                writer.WriteValue("Y2", mask.Y2);
                writer.WriteValue("X3", mask.X3);
                writer.WriteValue("Y3", mask.Y3);
                writer.WriteValue("X4", mask.X4);
                writer.WriteValue("Y4", mask.Y4);
                writer.WriteValue("AllowOnlyInside", mask.AllowOnlyInside);
                writer.WriteValue("Level", mask.Level);
                writer.WriteValue("Enabled", mask.Enabled);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    public static LaserProcessingSettings ReadFromJson(JToken token)
    {
        var settings = new LaserProcessingSettings();
        var optimizer = token["Optimizer"];
        if (optimizer != null)
        {
            settings.Optimizer = new OptimizerSettings
                                     {
                                         EnableOptimization = optimizer.ReadValueSafe("EnableOptimization", true),
                                         MaxJumpDistance = optimizer.ReadValueSafe("MaxJumpDistance", 5000f),
                                         BlankingDelayPoints = optimizer.ReadValueSafe("BlankingDelayPoints", 5),
                                         CornerAngleThreshold = optimizer.ReadValueSafe("CornerAngleThreshold", 30f),
                                         CornerAnchorCount = optimizer.ReadValueSafe("CornerAnchorCount", 3),
                                     };
        }

        var motion = token["Motion"];
        if (motion != null)
        {
            settings.Motion = new MotionSettings
                                  {
                                      DrawingSpeedUs = motion.ReadValueSafe("DrawingSpeedUs", 0f),
                                      MotionResponseUs = motion.ReadValueSafe("MotionResponseUs", 0f),
                                      CornerSettleUs = motion.ReadValueSafe("CornerSettleUs", 0f),
                                      TransitionUs = motion.ReadValueSafe("TransitionUs", 0f),
                                  };
        }

        var safety = token["Safety"];
        if (safety != null)
        {
            settings.Safety = new SafetySettings
                                  {
                                      // Absent = false: an uncalibrated laser streams only the alignment rectangle.
                                      Calibrated = safety.ReadValueSafe("Calibrated", false),
                                      BeamEnable = safety.ReadValueSafe("BeamEnable", true),
                                      SafeZoneCenterX = safety.ReadValueSafe("SafeZoneCenterX", 0f),
                                      SafeZoneCenterY = safety.ReadValueSafe("SafeZoneCenterY", 0f),
                                      SafeZoneWidth = safety.ReadValueSafe("SafeZoneWidth", 65534f),
                                      SafeZoneHeight = safety.ReadValueSafe("SafeZoneHeight", 65534f),
                                      MaxStepPerPoint = safety.ReadValueSafe("MaxStepPerPoint", 4000f),
                                      MaxStaticPoints = safety.ReadValueSafe("MaxStaticPoints", 64),
                                      MaxBrightness = safety.ReadValueSafe("MaxBrightness", 1f),
                                      ColorGainR = safety.ReadValueSafe("ColorGainR", 1f),
                                      ColorGainG = safety.ReadValueSafe("ColorGainG", 1f),
                                      ColorGainB = safety.ReadValueSafe("ColorGainB", 1f),
                                      ColorMinR = safety.ReadValueSafe("ColorMinR", 0f),
                                      ColorMinG = safety.ReadValueSafe("ColorMinG", 0f),
                                      ColorMinB = safety.ReadValueSafe("ColorMinB", 0f),
                                      ColorGammaR = safety.ReadValueSafe("ColorGammaR", 1f),
                                      ColorGammaG = safety.ReadValueSafe("ColorGammaG", 1f),
                                      ColorGammaB = safety.ReadValueSafe("ColorGammaB", 1f),
                                      TtlMode = safety.ReadValueSafe("TtlMode", false),
                                      MaxStaticIntensity = safety.ReadValueSafe("MaxStaticIntensity", 1f),
                                      FlipX = safety.ReadValueSafe("FlipX", false),
                                      FlipY = safety.ReadValueSafe("FlipY", false),
                                      ColorShiftR = safety.ReadValueSafe("ColorShiftR", 0),
                                      ColorShiftG = safety.ReadValueSafe("ColorShiftG", 0),
                                      ColorShiftB = safety.ReadValueSafe("ColorShiftB", 0),
                                      RenderSafetyShapes = safety.ReadValueSafe("RenderSafetyShapes", false),
                                      RenderShapesLevel = safety.ReadValueSafe("RenderShapesLevel", 0.1f),
                                      RenderShapesUseCustomColor = safety.ReadValueSafe("RenderShapesUseCustomColor", false),
                                      RenderShapesColorR = safety.ReadValueSafe("RenderShapesColorR", 1f),
                                      RenderShapesColorG = safety.ReadValueSafe("RenderShapesColorG", 0f),
                                      RenderShapesColorB = safety.ReadValueSafe("RenderShapesColorB", 0f),
                                      DesiredFps = safety.ReadValueSafe("DesiredFps", 60),
                                      SetupCenterX = safety.ReadValueSafe("SetupCenterX", 0f),
                                      SetupCenterY = safety.ReadValueSafe("SetupCenterY", 0f),
                                      SetupWidth = safety.ReadValueSafe("SetupWidth", 0.04f),
                                      SetupHeight = safety.ReadValueSafe("SetupHeight", 0.04f),
                                      FrameWarp = safety.ReadValueSafe("FrameWarp", false),
                                      WarpCorner1X = safety.ReadValueSafe("WarpCorner1X", -0.02f),
                                      WarpCorner1Y = safety.ReadValueSafe("WarpCorner1Y", 0.02f),
                                      WarpCorner2X = safety.ReadValueSafe("WarpCorner2X", 0.02f),
                                      WarpCorner2Y = safety.ReadValueSafe("WarpCorner2Y", 0.02f),
                                      WarpCorner3X = safety.ReadValueSafe("WarpCorner3X", 0.02f),
                                      WarpCorner3Y = safety.ReadValueSafe("WarpCorner3Y", -0.02f),
                                      WarpCorner4X = safety.ReadValueSafe("WarpCorner4X", -0.02f),
                                      WarpCorner4Y = safety.ReadValueSafe("WarpCorner4Y", -0.02f),
                                  };
            if (safety["Masks"] is JArray masks)
            {
                foreach (var maskToken in masks)
                {
                    settings.Safety.Masks.Add(new SafetySettings.MaskQuad
                                                  {
                                                      X1 = maskToken.ReadValueSafe("X1", 0f),
                                                      Y1 = maskToken.ReadValueSafe("Y1", 0f),
                                                      X2 = maskToken.ReadValueSafe("X2", 0f),
                                                      Y2 = maskToken.ReadValueSafe("Y2", 0f),
                                                      X3 = maskToken.ReadValueSafe("X3", 0f),
                                                      Y3 = maskToken.ReadValueSafe("Y3", 0f),
                                                      X4 = maskToken.ReadValueSafe("X4", 0f),
                                                      Y4 = maskToken.ReadValueSafe("Y4", 0f),
                                                      AllowOnlyInside = maskToken.ReadValueSafe("AllowOnlyInside", false),
                                                      Level = Math.Clamp(maskToken.ReadValueSafe("Level", 0f), 0f, 1f),
                                                      Enabled = maskToken.ReadValueSafe("Enabled", true),
                                                  });
                }
            }
        }

        return settings;
    }

    /// <summary>Path-ordering and blanking stage.</summary>
    public sealed class OptimizerSettings
    {
        public bool EnableOptimization = true;
        public float MaxJumpDistance = 5000f;
        public int BlankingDelayPoints = 5;
        public float CornerAngleThreshold = 30f;
        public int CornerAnchorCount = 3;

        public LaserOptimizerTransforms.Config ToConfig() => ToConfig(null, 0);

        /// <summary>The optimizer config plus the µs motion budgets converted at the bound
        /// plug's scan rate: a rate of 0 (unknown) keeps the dwell knobs off rather than
        /// guessing a device the output may not be bound to.</summary>
        public LaserOptimizerTransforms.Config ToConfig(MotionSettings? motion, int scanRatePps) => new()
                                                                                                     {
                                                                                                         EnableOptimization = EnableOptimization,
                                                                                                         MaxJumpDistance = MaxJumpDistance,
                                                                                                         BlankingDelayPoints = BlankingDelayPoints,
                                                                                                         CornerAngleThreshold = CornerAngleThreshold,
                                                                                                         CornerAnchorCount = CornerAnchorCount,
                                                                                                         MotionResponseUs = motion?.MotionResponseUs ?? 0f,
                                                                                                         CornerSettleUs = motion?.CornerSettleUs ?? 0f,
                                                                                                         TransitionUs = motion?.TransitionUs ?? 0f,
                                                                                                         PointsPerSecond = scanRatePps,
                                                                                                     };
    }

    /// <summary>
    /// The µs-based scanner motion budgets, edited in the Output Setup's Scanner Motion section
    /// and converted to dwell/resample point counts at the output's scan rate. All zero = off,
    /// which keeps the frame byte-identical to the pre-motion pipeline. These are per-output
    /// device settings (the scanners' physics), not per-content render knobs.
    /// </summary>
    public sealed class MotionSettings
    {
        /// <summary>Drawing speed in µs per 1000 ILDA units of drawn path (0 = off): the beam
        /// may not cross a segment faster than this budget - shorter segments are stretched
        /// with interpolated samples so the scanners can track them. Typical galvo budget is
        /// 6-12 µs per 1000 units; the default leaves the content's own point spacing in charge.</summary>
        public float DrawingSpeedUs;

        /// <summary>µs of lit dwell at every shape's first sample: the time the scanners need
        /// to settle after a jump before the beam opens. Typical 50-300 µs.</summary>
        public float MotionResponseUs;

        /// <summary>µs of extra dwell at sharp corners (on top of the corner anchors), so the
        /// corner renders sharp instead of rounded-off. Typical 50-200 µs.</summary>
        public float CornerSettleUs;

        /// <summary>µs of extra blanked hold at each shape entry (the transition between
        /// shapes), appended to the blanking ramp's settle. Typical 100-500 µs.</summary>
        public float TransitionUs;
    }

    /// <summary>Safety stage; <see cref="Calibrated"/>
    /// gates content vs. the manual alignment rectangle.</summary>
    public sealed class SafetySettings
    {
        public bool Calibrated;
        public bool BeamEnable = true;

        /// <summary>Safe-zone rect in ILDA units (±32767 field).</summary>
        public float SafeZoneCenterX;
        public float SafeZoneCenterY;
        public float SafeZoneWidth = 65534f;
        public float SafeZoneHeight = 65534f;

        public float MaxStepPerPoint = 4000f;
        public int MaxStaticPoints = 64;

        /// <summary>Show-level intensity ceiling (0..1); the plug's device ceiling stays the final audit.</summary>
        public float MaxBrightness = 1f;

        /// <summary>Per-channel projector calibration: gains shape each diode's level (0..2),
        /// minimums (0..1) cut a diode off below its emission threshold so near-black content
        /// stays black. Calibration, not protection - the safety limiters run unchanged.</summary>
        public float ColorGainR = 1f;
        public float ColorGainG = 1f;
        public float ColorGainB = 1f;
        public float ColorMinR;
        public float ColorMinG;
        public float ColorMinB;

        /// <summary>Per-channel gamma exponent (1 = linear, 2.2 = sRGB-style perceptual default);
        /// output = input^(1/gamma). Compensates diode brightness curves; per-channel values tune
        /// mixed direct-diode/DPSS systems. Calibration, not protection.</summary>
        public float ColorGammaR = 1f;
        public float ColorGammaG = 1f;
        public float ColorGammaB = 1f;

        /// <summary>TTL profile type: diodes without analog modulation are binary - at or above
        /// 50% the channel outputs its maximum, below it turns off. Gains/minimums/gamma are
        /// skipped in this mode.</summary>
        public bool TtlMode;

        /// <summary>Intensity fraction (0..1) a stationary beam is dimmed to after the dwell
        /// limit, instead of blanking - the venue-controlled "max non-moving beam intensity".
        /// 1 keeps the strict blank (the scan-fail default).</summary>
        public float MaxStaticIntensity = 1f;

        /// <summary>Output geometry flip (mirrors the frame on the projector).</summary>
        public bool FlipX;
        public bool FlipY;

        /// <summary>Per-channel time shift in points (negative = the color leads the beam),
        /// for diodes that react at different speeds. Calibration, like the gains.</summary>
        public int ColorShiftR;
        public int ColorShiftG;
        public int ColorShiftB;

        /// <summary>Manual alignment rectangle, in normalized field units: center -1..1, width/height
        /// 0..1 where 1.0 spans the full scanner field (-32767..32767). Starts tiny; the user extends
        /// it step by step before switching Calibrated on.</summary>
        public float SetupCenterX;
        public float SetupCenterY;
        public float SetupWidth = 0.04f;
        public float SetupHeight = 0.04f;

        /// <summary>Keystone: the frame lands on four draggable corners instead of the setup
        /// rectangle. While uncalibrated, the green alignment shape is the corner quad itself —
        /// trapeze it against a skewed screen; in calibrated mode the camera frame is mapped onto
        /// the quad with a homography. Corners are normalized field units (same as the setup rect),
        /// ordered top-left → top-right → bottom-right → bottom-left in field space (y up), and
        /// default to the setup rect's corners so switching on changes nothing at first.</summary>
        public bool FrameWarp;
        public float WarpCorner1X = -0.02f;
        public float WarpCorner1Y = 0.02f;
        public float WarpCorner2X = 0.02f;
        public float WarpCorner2Y = 0.02f;
        public float WarpCorner3X = 0.02f;
        public float WarpCorner3Y = -0.02f;
        public float WarpCorner4X = -0.02f;
        public float WarpCorner4Y = -0.02f;

        /// <summary>Safety masks: quads in normalized field units (same order as the warp
        /// corners) where the beam is blanked — or, in allow-only mode, the only place it may
        /// emit. Part of the safety stage: applied after the frame warp, before streaming, and
        /// no graph content can bypass them.</summary>
        public List<MaskQuad> Masks { get; } = [];

        /// <summary>When on, the safe-zone rectangle and the enabled mask outlines stream as
        /// faint shapes appended to the calibrated frame, so the protected regions can be
        /// verified against the real venue while aiming. The shapes are appended after the
        /// safety stage and stay inside the safe zone by construction.</summary>
        public bool RenderSafetyShapes;

        /// <summary>Brightness (0..1) of the rendered safety shapes. Kept low on purpose: the
        /// outline is an aiming aid, not content.</summary>
        public float RenderShapesLevel = 0.1f;

        /// <summary>Draw the safety shapes in the custom color below instead of the semantic
        /// defaults (safe zone green, masks orange) — for matching a show's look or a dim red.</summary>
        public bool RenderShapesUseCustomColor;

        /// <summary>Custom outline color for safety zone and masks, 0..1 per channel.</summary>
        public float RenderShapesColorR = 1f;
        public float RenderShapesColorG;
        public float RenderShapesColorB;

        /// <summary>
        /// Target content refresh rate for this output (frames per second the pump recomputes and
        /// submits new geometry). The scanner loops the last frame between updates, so the actual
        /// ILDA frame rate stays PPS / point count — this paces the updates and, with the point
        /// budget readout (PPS / desired FPS), tells whether the content fits the rate. Match it to
        /// a synced video's FPS; above ~45 FPS the scan is invisible, under ~35 the blinking shows.
        /// Scanning faster than the hardware likes hurts the scanners, so lower is safer.
        /// </summary>
        public int DesiredFps = 60;

        /// <summary>The ILDA frame rate a device actually achieves: its scan rate over the points
        /// in a frame (30 kpps / 500 points = 60 FPS). 0 for an empty frame.</summary>
        public static int EffectiveFrameRate(int pointsPerSecond, int pointCount)
            => pointCount > 0 ? Math.Clamp(pointsPerSecond, 0, 100000) / pointCount : 0;

        private int _masksVersion;
        private int _configuredMasksVersion = -1;
        private float[]? _maskCorners;
        private bool[]? _maskAllowOnly;
        private float[]? _maskLevels;
        private int _maskCount;

        /** Bump after any change to <see cref="Masks"/> so the next ToConfig rebuilds the ILDA arrays. */
        public void MasksChanged() => _masksVersion++;

        public LaserSafetyTransforms.Config ToConfig()
        {
            if (_configuredMasksVersion != _masksVersion)
                RebuildMaskArrays();

            return new()
                       {
                           BeamEnable = BeamEnable,
                           ZoneCenterX = SafeZoneCenterX,
                           ZoneCenterY = SafeZoneCenterY,
                           ZoneHalfWidth = MathF.Max(0f, SafeZoneWidth) * 0.5f,
                           ZoneHalfHeight = MathF.Max(0f, SafeZoneHeight) * 0.5f,
                           MaxStepPerPoint = Math.Clamp(MaxStepPerPoint, 100f, 65535f),
                           MaxStaticPoints = Math.Clamp(MaxStaticPoints, 1, 100000),
                           StaticDistance = 5,
                           MaxBrightness = Math.Clamp(MaxBrightness, 0f, 1f),
                           ColorGainR = ColorGainR,
                           ColorGainG = ColorGainG,
                           ColorGainB = ColorGainB,
                           ColorMinR = ColorMinR,
                           ColorMinG = ColorMinG,
                           ColorMinB = ColorMinB,
                           ColorGammaR = ColorGammaR,
                           ColorGammaG = ColorGammaG,
                           ColorGammaB = ColorGammaB,
                           TtlMode = TtlMode,
                           MaxStaticIntensity = MaxStaticIntensity,
                           FlipX = FlipX,
                           FlipY = FlipY,
                           ColorShiftR = ColorShiftR,
                           ColorShiftG = ColorShiftG,
                           ColorShiftB = ColorShiftB,
                           MaskCorners = _maskCorners,
                           MaskAllowOnlyInside = _maskAllowOnly,
                           MaskLevels = _maskLevels,
                           MaskCount = _maskCount,
                       };
        }

        /// <summary>The masks list is read-only to the pipeline: this copies the enabled ones into
        /// reused ILDA-unit arrays so the per-pump ToConfig stays allocation-free.</summary>
        private void RebuildMaskArrays()
        {
            var enabled = 0;
            for (var i = 0; i < Masks.Count; i++)
            {
                if (Masks[i].Enabled)
                    enabled++;
            }

            if (enabled == 0)
            {
                _maskCorners = null;
                _maskAllowOnly = null;
                _maskLevels = null;
                _maskCount = 0;
                _configuredMasksVersion = _masksVersion;
                return;
            }

            if (_maskCorners == null || _maskCorners.Length != enabled * 8)
            {
                _maskCorners = new float[enabled * 8];
                _maskAllowOnly = new bool[enabled];
                _maskLevels = new float[enabled];
            }

            var n = 0;
            for (var i = 0; i < Masks.Count; i++)
            {
                var mask = Masks[i];
                if (!mask.Enabled)
                    continue;

                var offset = n * 8;
                _maskCorners[offset] = Math.Clamp(mask.X1, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 1] = Math.Clamp(mask.Y1, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 2] = Math.Clamp(mask.X2, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 3] = Math.Clamp(mask.Y2, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 4] = Math.Clamp(mask.X3, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 5] = Math.Clamp(mask.Y3, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 6] = Math.Clamp(mask.X4, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskCorners[offset + 7] = Math.Clamp(mask.Y4, -1f, 1f) * LaserSafetyTransforms.FieldMax;
                _maskAllowOnly[n] = mask.AllowOnlyInside;
                _maskLevels[n] = Math.Clamp(mask.Level, 0f, 1f);
                n++;
            }

            _maskCount = n;
            _configuredMasksVersion = _masksVersion;
        }

        public string DescribeSetup() =>
            $"Safety setup (manual): {SetupWidth:F2} x {SetupHeight:F2} field at ({SetupCenterX:F2}, {SetupCenterY:F2})"
            + " - switch Calibrated on for content";

        /// <summary>One safety mask quad. Corners are normalized field units (-1..1, same order
        /// as the warp corners) and may be concave.</summary>
        public sealed class MaskQuad
        {
            public float X1;
            public float Y1;
            public float X2;
            public float Y2;
            public float X3;
            public float Y3;
            public float X4;
            public float Y4;

            /// <summary>False = shape inside (protect an area). True = allow only inside (everything outside is blanked).</summary>
            public bool AllowOnlyInside;

            /// <summary>Blank masks only: the beam level kept inside (0..1). 0 blanks; a small
            /// value like 0.05 keeps a faint beam - the reduced-level audience-scanning case.</summary>
            public float Level;

            public bool Enabled = true;

            public static MaskQuad Centered(float halfExtent = 0.1f) => new()
                                                                            {
                                                                                X1 = -halfExtent, Y1 = halfExtent,
                                                                                X2 = halfExtent, Y2 = halfExtent,
                                                                                X3 = halfExtent, Y3 = -halfExtent,
                                                                                X4 = -halfExtent, Y4 = -halfExtent,
                                                                            };
        }
    }
}
