// File: PointsToDMXLights.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Utils;

namespace Lib.io.dmx
{
    // ------------------------------------------------------------------------
    //  Type aliases – keep System.Numerics types distinct from SharpDX types.
    // ------------------------------------------------------------------------
    using Vec3 = System.Numerics.Vector3;
    using Quat = System.Numerics.Quaternion;

    [Guid("D86E12A7-C3D1-46E3-A470-3808725C7858")]
    public sealed class PointsToDMXLights : Instance<PointsToDMXLights>
    {
        #region Output Slots
        [Output(Guid = "8DC2DB32-D7A3-4B3A-A000-93C3107D19E4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<int>> Result = new(new List<int>(20));

        [Output(Guid = "DA7DEB8C-4218-4CAE-9EC5-FD7C2E6F4C35")]
        public readonly Slot<BufferWithViews> VisualizeLights = new();
        #endregion

        #region Enums
        public enum AxisModes { Disabled, X, Y, Z }
        public enum RotationOrderModes { PanThenTilt, TiltThenPan }
        public enum ForwardVectorModes { X, Y, Z, NegX, NegY, NegZ }

        public enum TestMode
        {
            Disabled = 0,
            Z_Positive,
            Z_Negative,
            X_Positive,
            X_Negative,
            Y_Positive,
            Y_Negative
        }
        #endregion

        #region Private Fields & Constants
        private readonly List<int> _resultItems = new(128);
        private const int UniverseSize = 512;

        private BufferWithViews _visualizeBuffer;
        private Point[] _visualizationPoints = Array.Empty<Point>();

        // Stores the previous pan/tilt (radians) – used by shortest‑path logic.
        private Vector2 _lastPanTilt = new Vector2(float.NaN, float.NaN);

        private Point[] _points = Array.Empty<Point>();
        private Point[] _referencePoints = Array.Empty<Point>();

        private readonly List<int> _pointChannelValues = new();
        private readonly StructuredBufferReadAccess _pointsBufferReader = new();
        private readonly StructuredBufferReadAccess _referencePointsBufferReader = new();

        // Cached forward axis – may be overridden by TestMode.
        private Vec3 _cachedForwardAxis = Vec3.UnitZ;
        #endregion

        #region Constructor
        public PointsToDMXLights()
        {
            Result.UpdateAction = Update;
        }
        #endregion

        #region Input Slots
        // Buffers
        [Input(Guid = "61B48E46-C3D1-46E3-A470-810D55F30AA6")]
        public readonly InputSlot<BufferWithViews> EffectedPoints = new();

        [Input(Guid = "2BEA2CCB-89F2-427B-BD9A-95C7038B715E")]
        public readonly InputSlot<BufferWithViews> ReferencePoints = new();

        // General behaviour
        [Input(Guid = "1348ED7C-79F8-48C6-AC00-E60FB40050DB")]
        public readonly InputSlot<int> FixtureChannelSize = new();

        [Input(Guid = "7449CD05-54BE-484B-854A-D2143340F925")]
        public readonly InputSlot<bool> FitInUniverse = new();

        [Input(Guid = "850AF6C3-D9EF-492C-9CFB-E2589AE5B9AC")]
        public readonly InputSlot<bool> FillUniverse = new();

        [Input(Guid = "23F23213-68E2-45F5-B452-4A86289004C0")]
        public readonly InputSlot<bool> DebugToLog = new();

        // Test‑Mode dropdown (debug only)
        [Input(Guid = "D8A90C30-4E5B-4F0B-BFA7-09DAF3A4C71F", MappedType = typeof(TestMode))]
        public readonly InputSlot<int> TestModeSelect = new();

        // POSITION
        [Input(Guid = "DF04FCE0-C6E5-4039-B03F-E651FC0EC4A9")]
        public readonly InputSlot<bool> GetPosition = new();

        [Input(Guid = "628D96A8-466B-4148-9658-7786833EC989", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> PositionMeasureAxis = new();

        [Input(Guid = "78A7E683-F4E7-4826-8E39-C8DE08E50E5E")]
        public readonly InputSlot<bool> InvertPositionDirection = new();

        [Input(Guid = "8880C101-403F-46E0-901E-20EC2DD333E9")]
        public readonly InputSlot<Vector2> PositionDistanceRange = new();

        [Input(Guid = "FC3EC0D6-8567-4D5F-9A63-5C69FB5988CB")]
        public readonly InputSlot<int> PositionChannel = new();

        [Input(Guid = "658A19DF-E51B-45B4-9F91-CB97A891255B")]
        public readonly InputSlot<int> PositionFineChannel = new();

        // ROTATION
        [Input(Guid = "4922ACD8-AB83-4394-8118-C555385C2CE9")]
        public readonly InputSlot<bool> GetRotation = new();

        [Input(Guid = "032F3617-E1F3-4B41-A3BE-61DD63B9F3BA", MappedType = typeof(ForwardVectorModes))]
        public readonly InputSlot<int> ForwardVector = new();

        [Input(Guid = "9C235473-346B-4861-9844-4B584E09F58A", MappedType = typeof(RotationOrderModes))]
        public readonly InputSlot<int> RotationOrder = new();

        [Input(Guid = "49FEFBDB-2652-43DB-AE52-EBC2DF3E2856")]
        public readonly InputSlot<bool> InvertX = new();

        [Input(Guid = "6D8FC457-0C80-4736-8C25-CC48F07CBBFD")]
        public readonly InputSlot<bool> InvertY = new();

        [Input(Guid = "0C57CDD5-E450-4425-954F-C9E4256F83E1")]
        public readonly InputSlot<bool> InvertZ = new();

        [Input(Guid = "1F532994-FB0E-44E4-8A80-7917E1851EAE", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> PanAxis = new();

        [Input(Guid = "7BF3E057-B9EB-43D2-8E1A-64C1C3857CA1")]
        public readonly InputSlot<bool> InvertPan = new();

        [Input(Guid = "1F877CF6-10D9-4D0B-B087-974BD6855E0A", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> TiltAxis = new();

        [Input(Guid = "F85ECF9F-0C3D-4C10-8BA7-480AA2C7A667")]
        public readonly InputSlot<bool> InvertTilt = new();

        [Input(Guid = "E96655BE-6BC7-4CA4-BF74-079A07570D74")]
        public readonly InputSlot<bool> ShortestPathPanTilt = new();

        [Input(Guid = "F50DA250-606D-4A15-A25E-5458F540E527")]
        public readonly InputSlot<Vector2> PanRange = new();

        [Input(Guid = "9000C279-73E4-4DE8-A1F8-C3914EAAF533")]
        public readonly InputSlot<int> PanChannel = new();

        [Input(Guid = "4D4B3425-E6AD-4834-A8A7-06C9F9C2B909")]
        public readonly InputSlot<int> PanFineChannel = new();

        [Input(Guid = "6E8B4125-0E8C-430B-897D-2231BB4C8F6F")]
        public readonly InputSlot<Vector2> TiltRange = new();

        [Input(Guid = "47D7294F-6F73-4E21-AC9A-0FC0817283FB")]
        public readonly InputSlot<int> TiltChannel = new();

        [Input(Guid = "4A40E022-D206-447C-BDA3-D534F231C817")]
        public readonly InputSlot<int> TiltFineChannel = new();

        [Input(Guid = "C9D7CD19-7FC6-4491-8DFA-3808725C7859")]
        public readonly InputSlot<float> PanOffset = new();

        [Input(Guid = "C9D7CD19-7FC6-4491-8DFA-3808725C7860")]
        public readonly InputSlot<float> TiltOffset = new();

        // VISUALIZATION SETTINGS
        [Input(Guid = "294B0515-B9F2-446A-8A97-01E3C8B715C0", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> VisPanAxis = new();


        [Input(Guid = "F98E8F19-C234-453D-9492-369F6B08035D", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> VisTiltAxis = new();

        // COLOUR
        [Input(Guid = "5CDC69F7-45EC-4EEC-BFB6-960D6245DAFB")]
        public readonly InputSlot<bool> GetColor = new();

        [Input(Guid = "CF2C3308-8F3F-442D-A563-B419F12E7AD1")]
        public readonly InputSlot<bool> RGBToCMY = new();

        [Input(Guid = "013CC355-91D6-4EA6-B9F7-F1817B89E4A3")]
        public readonly InputSlot<int> RedChannel = new();

        [Input(Guid = "970769F4-116F-418D-87A7-CDA28E44D063")]
        public readonly InputSlot<int> GreenChannel = new();

        [Input(Guid = "D755342B-9A9E-4C78-8376-81579D8C0909")]
        public readonly InputSlot<int> BlueChannel = new();

        [Input(Guid = "F13EDEBD-B44F-49E9-985E-7E3FEB886FEA")]
        public readonly InputSlot<int> AlphaChannel = new();

        [Input(Guid = "8CEECE78-9A08-4C7B-8FEA-740E8E5929A6")]
        public readonly InputSlot<int> WhiteChannel = new();

        [Input(Guid = "5E96A7A3-5340-43F2-96B9-9972A69421E5")]
        public readonly InputSlot<bool> Is16BitColor = new();

        // FEATURES (F1 / F2)
        [Input(Guid = "91C78090-BE10-4203-827E-D2EF1B93317E")]
        public readonly InputSlot<bool> GetF1 = new();

        [Input(Guid = "BEC9E5A6-40A9-49B2-88BD-01A4EA03D28C")]
        public readonly InputSlot<bool> GetF1ByPixel = new();

        [Input(Guid = "B7061834-66AA-4F7F-91F9-10EBFE16713F")]
        public readonly InputSlot<int> F1Channel = new();

        [Input(Guid = "1CB93E97-0161-4A77-BBC7-FF30C1972CF8")]
        public readonly InputSlot<bool> GetF2 = new();

        [Input(Guid = "B8080F4E-4542-4E20-9844-8028BBAF223F")]
        public readonly InputSlot<bool> GetF2ByPixel = new();

        [Input(Guid = "D77BE0D1-5FB9-4D26-9E4A-E16497E4759C")]
        public readonly InputSlot<int> F2Channel = new();

        // CUSTOM VARIABLES
        [Input(Guid = "25E5F0CE-5EC8-4C99-BEB1-317C6911A128")]
        public readonly InputSlot<bool> SetCustomVar1 = new();

        [Input(Guid = "B08C920F-0D6B-4820-BC2D-81A47D5F1147")]
        public readonly InputSlot<int> CustomVar1Channel = new();

        [Input(Guid = "50E849E8-5582-432E-98F7-D8E036273864")]
        public readonly InputSlot<int> CustomVar1 = new();

        [Input(Guid = "18CC3A73-3A1A-4370-87B7-E54CD44F4A3A")]
        public readonly InputSlot<bool> SetCustomVar2 = new();

        [Input(Guid = "098F1662-6F47-4DD0-9A73-4C4814A6EF23")]
        public readonly InputSlot<int> CustomVar2Channel = new();

        [Input(Guid = "E7A48FE0-D788-4F12-A9D4-52472519DA09")]
        public readonly InputSlot<int> CustomVar2 = new();

        [Input(Guid = "876EF5B5-F2C6-4501-9E55-00B9A553A2E3")]
        public readonly InputSlot<bool> SetCustomVar3 = new();

        [Input(Guid = "AC9A709E-6DC0-40CA-9F70-350E655A2630")]
        public readonly InputSlot<int> CustomVar3Channel = new();

        [Input(Guid = "D16D7C5C-2795-4FDE-85FD-13B515191FBE")]
        public readonly InputSlot<int> CustomVar3 = new();

        [Input(Guid = "8DD3FC1C-CD94-4BF0-B948-D6F734916D49")]
        public readonly InputSlot<bool> SetCustomVar4 = new();

        [Input(Guid = "CBAF821C-0305-4C74-A632-864081CC9A34")]
        public readonly InputSlot<int> CustomVar4Channel = new();

        [Input(Guid = "B29EBE11-89CB-4F86-AEE0-CF729FA0D62C")]
        public readonly InputSlot<int> CustomVar4 = new();

        [Input(Guid = "A9315F88-6024-42E9-9691-4544627F0BEF")]
        public readonly InputSlot<bool> SetCustomVar5 = new();

        [Input(Guid = "7C59A5FB-052A-443C-9E10-CF859FE25658")]
        public readonly InputSlot<int> CustomVar5Channel = new();

        [Input(Guid = "58CC3EEE-E81E-4BAB-B12C-E7BC3CF62DD0")]
        public readonly InputSlot<int> CustomVar5 = new();
        #endregion

        #region Main Update Method
        private void Update(EvaluationContext context)
        {
            var pointBuffer = EffectedPoints.GetValue(context);
            var referencePointBuffer = ReferencePoints.GetValue(context);

            if (pointBuffer == null)
            {
                Log.Warning("EffectedPoints buffer is not connected.", this);
                Result.Value?.Clear();
                VisualizeLights.Value = null;
                _lastPanTilt = new Vector2(float.NaN, float.NaN);
                return;
            }

            // Asynchronously read the structured buffers
            _pointsBufferReader.InitiateRead(
                pointBuffer.Buffer,
                pointBuffer.Srv.Description.Buffer.ElementCount,
                pointBuffer.Buffer.Description.StructureByteStride,
                OnPointsReadComplete);
            _pointsBufferReader.Update();

            if (referencePointBuffer != null)
            {
                _referencePointsBufferReader.InitiateRead(
                    referencePointBuffer.Buffer,
                    referencePointBuffer.Srv.Description.Buffer.ElementCount,
                    referencePointBuffer.Buffer.Description.StructureByteStride,
                    OnReferencePointsReadComplete);
                _referencePointsBufferReader.Update();
            }
            else
            {
                _referencePoints = Array.Empty<Point>();
            }

            // Process when we actually have points
            if (_points != null && _points.Length > 0)
            {
                if (_visualizationPoints.Length != _points.Length)
                    _visualizationPoints = new Point[_points.Length];

                ApplyTestMode(context);

                UpdateChannelData(context, _points);
                Result.Value = new List<int>(_resultItems);

                UpdateVisualizationBuffer();
                VisualizeLights.Value = _visualizeBuffer;
            }
            else
            {
                Result.Value?.Clear();
                VisualizeLights.Value = null;
                _lastPanTilt = new Vector2(float.NaN, float.NaN);
            }
        }
        #endregion

        #region Test‑Mode handling
        private void ApplyTestMode(EvaluationContext context)
        {
            var mode = (TestMode)TestModeSelect.GetValue(context);

            switch (mode)
            {
                case TestMode.Disabled:
                    _cachedForwardAxis = ResolveForwardFromInput(context);
                    break;
                case TestMode.Z_Positive:
                    _cachedForwardAxis = Vec3.UnitZ;
                    break;
                case TestMode.Z_Negative:
                    _cachedForwardAxis = -Vec3.UnitZ;
                    break;
                case TestMode.X_Positive:
                    _cachedForwardAxis = Vec3.UnitX;
                    break;
                case TestMode.X_Negative:
                    _cachedForwardAxis = -Vec3.UnitX;
                    break;
                case TestMode.Y_Positive:
                    _cachedForwardAxis = Vec3.UnitY;
                    break;
                case TestMode.Y_Negative:
                    _cachedForwardAxis = -Vec3.UnitY;
                    break;
            }
        }

        private Vec3 ResolveForwardFromInput(EvaluationContext context)
        {
            var mode = (ForwardVectorModes)ForwardVector.GetValue(context);
            return mode switch
            {
                ForwardVectorModes.X => Vec3.UnitX,
                ForwardVectorModes.Y => Vec3.UnitY,
                ForwardVectorModes.Z => Vec3.UnitZ,
                ForwardVectorModes.NegX => -Vec3.UnitX,
                ForwardVectorModes.NegY => -Vec3.UnitY,
                ForwardVectorModes.NegZ => -Vec3.UnitZ,
                _ => Vec3.UnitZ,
            };
        }
        #endregion

        #region Buffer‑Read Callbacks
        private void OnPointsReadComplete(StructuredBufferReadAccess.ReadRequestItem readItem,
                                          IntPtr dataPointer,
                                          DataStream dataStream)
        {
            int count = readItem.ElementCount;
            if (_points.Length != count)
                _points = new Point[count];

            using (dataStream) { dataStream.ReadRange(_points, 0, count); }
        }

        private void OnReferencePointsReadComplete(StructuredBufferReadAccess.ReadRequestItem readItem,
                                                 IntPtr dataPointer,
                                                 DataStream dataStream)
        {
            int count = readItem.ElementCount;
            if (_referencePoints.Length != count)
                _referencePoints = new Point[count];

            using (dataStream) { dataStream.ReadRange(_referencePoints, 0, count); }
        }
        #endregion

        #region Channel‑Data Generation
        private void UpdateChannelData(EvaluationContext context, Point[] points)
        {
            int fixtureChannelSize = FixtureChannelSize.GetValue(context);
            int effectedPointsCount = points.Length;
            bool debugToLog = DebugToLog.GetValue(context);

            // Determine fixture → pixel mapping (reference based)
            int fixtureCount;
            int pixelsPerFixture;
            bool useReferencePoints = _referencePoints.Length > 0;

            if (useReferencePoints)
            {
                fixtureCount = _referencePoints.Length;
                if (fixtureCount == 0 || effectedPointsCount % fixtureCount != 0)
                {
                    Log.Warning(
                        $"Effected points count ({effectedPointsCount}) is not a multiple of reference points count ({fixtureCount}). " +
                        "Falling back to 1‑to‑1 mapping.", this);
                    fixtureCount = effectedPointsCount;
                    pixelsPerFixture = 1;
                    useReferencePoints = false;
                }
                else
                {
                    pixelsPerFixture = effectedPointsCount / fixtureCount;
                }
            }
            else
            {
                fixtureCount = effectedPointsCount;
                pixelsPerFixture = 1;
            }

            bool fitInUniverse = FitInUniverse.GetValue(context);
            bool fillUniverse = FillUniverse.GetValue(context);

            _resultItems.Clear();
            _pointChannelValues.Clear();

            if (fixtureChannelSize <= 0)
            {
                if (effectedPointsCount > 0) Log.Warning("FixtureChannelSize is 0 or less, no DMX output generated.", this);
                return;
            }

            // Pre‑allocate the per‑fixture DMX channel buffer
            _pointChannelValues.Capacity = fixtureChannelSize;
            for (int i = 0; i < fixtureChannelSize; i++)
                _pointChannelValues.Add(0);

            // Process each fixture
            for (int fixtureIdx = 0; fixtureIdx < fixtureCount; fixtureIdx++)
            {
                bool logThisFixture = debugToLog && fixtureIdx == 0;

                // Reset per‑fixture channel array
                for (int i = 0; i < fixtureChannelSize; i++)
                    _pointChannelValues[i] = 0;

                int firstPixelIdx = fixtureIdx * pixelsPerFixture;
                Point transformPoint = points[firstPixelIdx];
                Point referencePoint = useReferencePoints ? _referencePoints[fixtureIdx] : transformPoint;

                if (logThisFixture) Log.Debug("--- Fixture 0 Debug ---", this);

                // Process transformations
                Vec3 finalVisPos;
                Quat finalVisOrientation = ProcessTransformations(context,
                                                                  transformPoint,
                                                                  referencePoint,
                                                                  useReferencePoints,
                                                                  logThisFixture,
                                                                  out finalVisPos);

                // Store visualisation data
                for (int p = 0; p < pixelsPerFixture; ++p)
                {
                    int curIdx = firstPixelIdx + p;
                    if (curIdx < _visualizationPoints.Length)
                    {
                        Point currentPoint = points[curIdx];
                        currentPoint.Position = finalVisPos;
                        currentPoint.Orientation = finalVisOrientation;
                        _visualizationPoints[curIdx] = currentPoint;
                    }
                }

                // Handle color, features, and custom variables
                HandleColorAndFeatures(context, points, transformPoint, firstPixelIdx, pixelsPerFixture);
                HandleCustomVariables(context);

                // Universe fit
                if (fitInUniverse)
                {
                    int remaining = UniverseSize - (_resultItems.Count % UniverseSize);
                    if (fixtureChannelSize > remaining)
                    {
                        for (int i = 0; i < remaining; i++)
                            _resultItems.Add(0);
                    }
                }

                // Append fixture's DMX channel list
                _resultItems.AddRange(_pointChannelValues);
            }

            // Universe fill
            if (fillUniverse)
            {
                int remainder = _resultItems.Count % UniverseSize;
                if (remainder != 0)
                {
                    int toAdd = UniverseSize - remainder;
                    for (int i = 0; i < toAdd; i++)
                        _resultItems.Add(0);
                }
            }
        }
        #endregion

        #region Transformations (Position & Rotation)
        private Quat ProcessTransformations(EvaluationContext context,
                                            Point transformPoint,
                                            Point referencePoint,
                                            bool useReferencePoints,
                                            bool shouldLog,
                                            out Vec3 finalVisPosition)
        {
            bool getRot = GetRotation.GetValue(context);
            bool getPos = GetPosition.GetValue(context);

            // Rotation
            Quat finalOrientation = transformPoint.Orientation;
            if (getRot)
                finalOrientation = ProcessRotation(context,
                                                   transformPoint,
                                                   referencePoint,
                                                   useReferencePoints,
                                                   shouldLog);

            // Position
            finalVisPosition = transformPoint.Position;
            if (getPos)
                finalVisPosition = ProcessPosition(context,
                                                   transformPoint,
                                                   referencePoint,
                                                   useReferencePoints,
                                                   shouldLog);
            else if (getRot)
                finalVisPosition = referencePoint.Position;

            return finalOrientation;
        }
        #endregion

        #region Position Handling
        private Vec3 ProcessPosition(EvaluationContext context,
                                     Point point,
                                     Point referencePoint,
                                     bool calculateRelativePosition,
                                     bool shouldLog)
        {
            int channel = PositionChannel.GetValue(context);
            int fineChannel = PositionFineChannel.GetValue(context);
            AxisModes axis = (AxisModes)PositionMeasureAxis.GetValue(context);

            if (channel <= 0 || axis == AxisModes.Disabled)
                return point.Position;

            bool invert = InvertPositionDirection.GetValue(context);
            Vector2 range = PositionDistanceRange.GetValue(context);

            if (Math.Abs(range.Y - range.X) < 1e-4f)
            {
                Log.Warning("PositionDistanceRange min and max are too close – will output 0.", this);
                SetDmxValue(0f, channel, fineChannel, range.X, range.Y, shouldLog, "Position");
                return point.Position;
            }

            Vec3 pos = point.Position;
            Vec3 refPos = calculateRelativePosition ? referencePoint.Position : Vec3.Zero;

            float distance = axis switch
            {
                AxisModes.X => pos.X - refPos.X,
                AxisModes.Y => pos.Y - refPos.Y,
                AxisModes.Z => pos.Z - refPos.Z,
                _ => 0f,
            };

            if (invert) distance = -distance;

            float clampedDist = Math.Clamp(distance, range.X, range.Y);

            SetDmxValue(clampedDist,
                        channel,
                        fineChannel,
                        range.X,
                        range.Y,
                        shouldLog,
                        "Position");

            // Compute position for visualization
            Vec3 resultPosition = point.Position;
            float finalDist = clampedDist;
            switch (axis)
            {
                case AxisModes.X: resultPosition.X = refPos.X + finalDist; break;
                case AxisModes.Y: resultPosition.Y = refPos.Y + finalDist; break;
                case AxisModes.Z: resultPosition.Z = refPos.Z + finalDist; break;
            }

            return resultPosition;
        }
        #endregion

        #region Rotation Handling
        private Quat ProcessRotation(EvaluationContext context,
                                     Point point,
                                     Point referencePoint,
                                     bool calculateRelativeRotation,
                                     bool shouldLog)
        {
            // Axis configuration
            AxisModes panAxis = (AxisModes)PanAxis.GetValue(context);
            AxisModes tiltAxis = (AxisModes)TiltAxis.GetValue(context);

            // Validate axes first
            if (!ValidateAxes(panAxis, tiltAxis))
                return point.Orientation;

            int panChannel = PanChannel.GetValue(context);
            int panFineChannel = PanFineChannel.GetValue(context);
            int tiltChannel = TiltChannel.GetValue(context);
            int tiltFineChannel = TiltFineChannel.GetValue(context);

            bool panEnabled = panAxis != AxisModes.Disabled && panChannel > 0;
            bool tiltEnabled = tiltAxis != AxisModes.Disabled && tiltChannel > 0;

            if (shouldLog) Log.Debug($"Processing Rotation for Fixture. PanEnabled: {panEnabled}, TiltEnabled: {tiltEnabled}, Pan16Bit: {panFineChannel > 0}, Tilt16Bit: {tiltFineChannel > 0}", this);

            // Active quaternion (relative handling)
            Quat active = ComputeActiveRotation(point.Orientation,
                                                referencePoint.Orientation,
                                                calculateRelativeRotation);
            if (shouldLog) Log.Debug($"Active Quaternion: {active}", this);

            // Direction extraction
            Vec3 direction = ExtractDirection(active,
                                              _cachedForwardAxis,
                                              InvertX.GetValue(context),
                                              InvertY.GetValue(context),
                                              InvertZ.GetValue(context));
            if (shouldLog) Log.Debug($"Extracted Direction Vector: {direction}", this);

            // Raw pan/tilt from direction - IK LOGIC THAT WORKS FOR DMX
            var (rawPan, rawTilt) = ComputePanTiltAngles(direction,
                                                         panAxis,
                                                         tiltAxis,
                                                         shouldLog);
            if (shouldLog) Log.Debug($"Computed raw angles from direction - Pan: {rawPan * 180f / MathF.PI:F2} deg ({rawPan:F4} rad), Tilt: {rawTilt * 180f / MathF.PI:F2} deg ({rawTilt:F4} rad)", this);

            // Apply pan and tilt offsets
            float panOffsetRad = PanOffset.GetValue(context) * MathF.PI / 180f;
            float tiltOffsetRad = TiltOffset.GetValue(context) * MathF.PI / 180f;
            rawPan += panOffsetRad;
            rawTilt += tiltOffsetRad;
            if (shouldLog) Log.Debug($"Angles after applying offsets - Pan: {rawPan * 180f / MathF.PI:F2} deg ({rawPan:F4} rad), Tilt: {rawTilt * 180f / MathF.PI:F2} deg ({rawTilt:F4} rad)", this);

            // Apply ranges, inversion, shortest‑path and write DMX
            float finalPan = 0f, finalTilt = 0f;
            bool useShortestPath = ShortestPathPanTilt.GetValue(context);

            if (panEnabled)
            {
                finalPan = ApplyPanRangeAndWrite(context,
                                                rawPan,
                                                panChannel,
                                                panFineChannel,
                                                PanRange.GetValue(context),
                                                InvertPan.GetValue(context),
                                                useShortestPath,
                                                _lastPanTilt.X,
                                                shouldLog);
            }
            else
            {
                _lastPanTilt.X = float.NaN;
            }

            if (tiltEnabled)
            {
                finalTilt = ApplyTiltRangeAndWrite(context,
                                                  rawTilt,
                                                  tiltChannel,
                                                  tiltFineChannel,
                                                  TiltRange.GetValue(context),
                                                  InvertTilt.GetValue(context),
                                                  useShortestPath,
                                                  _lastPanTilt.Y,
                                                  shouldLog);
            }
            else
            {
                _lastPanTilt.Y = float.NaN;
            }

            // --- Visualization Calculation START ---
            // finalPan/finalTilt: DMX value (Raw Angle + Offset + Clamping)
            // panAngleForViz/tiltAngleForViz: Physical Angle (Raw Angle + Clamping)
            float panAngleForViz = finalPan - panOffsetRad;
            float tiltAngleForViz = finalTilt - tiltOffsetRad;

            // Pan Inversion (to fix visualization mirror/inversion)
            if (panEnabled)
            {
                panAngleForViz = -panAngleForViz; // Invert Pan angle for visualization
            }


            // Get visualization axes (fall back to DMX axes if Disabled)

            AxisModes visPanAxis = (AxisModes)VisPanAxis.GetValue(context);

            AxisModes visTiltAxis = (AxisModes)VisTiltAxis.GetValue(context);


            // Create Pan and Tilt Quaternions using the corrected physical angles
            Quat panQuat = panEnabled
                ? Quat.CreateFromAxisAngle(GetAxisVector(visPanAxis), panAngleForViz)
                : Quat.Identity;
            Quat tiltQuat = tiltEnabled
                ? Quat.CreateFromAxisAngle(GetAxisVector(visTiltAxis), tiltAngleForViz)
                : Quat.Identity;
            // --- Visualization Calculation END ---

            // Re‑assemble final rotation (relative to neutral orientation)
            Quat resultRotation = (RotationOrderModes)RotationOrder.GetValue(context) == RotationOrderModes.TiltThenPan
                ? tiltQuat * panQuat
                : panQuat * tiltQuat;

            // Apply reference orientation if relative mode is active (ABSOLUTE FINAL ORIENTATION)
            Quat finalOrientation = resultRotation;
            if (calculateRelativeRotation)
                finalOrientation = referencePoint.Orientation * resultRotation;

            if (shouldLog) Log.Debug($"Re-assembled final orientation: {finalOrientation}", this);

            return finalOrientation;
        }

        #region Rotation Helper Methods
        private bool ValidateAxes(AxisModes pan, AxisModes tilt)
        {
            if (pan == AxisModes.Disabled && tilt == AxisModes.Disabled)
            {
                Log.Warning("Both Pan and Tilt axes are disabled – rotation will be ignored.", this);
                return false;
            }

            if (pan != AxisModes.Disabled && pan == tilt)
            {
                Log.Warning($"Pan and Tilt axes cannot be identical ({pan}). Skipping rotation.", this);
                return false;
            }

            Vec3 panVec = GetAxisVector(pan);
            Vec3 tiltVec = GetAxisVector(tilt);
            if (panVec != Vec3.Zero && tiltVec != Vec3.Zero &&
                Vec3.Cross(panVec, tiltVec).LengthSquared() < 1e-6f)
            {
                Log.Warning($"Pan ({pan}) and Tilt ({tilt}) axes are collinear – rotation undefined.", this);
                return false;
            }
            return true;
        }

        private Quat ComputeActiveRotation(Quat current, Quat reference, bool relative)
        {
            if (!relative) return current;

            if (float.IsNaN(reference.X) || float.IsNaN(reference.Y) ||
                float.IsNaN(reference.Z) || float.IsNaN(reference.W))
            {
                Log.Warning("Reference rotation is invalid (NaN components). Falling back to absolute rotation.", this);
                return current;
            }

            return Quat.Inverse(reference) * current;
        }

        private Vec3 ExtractDirection(Quat rotation,
                                      Vec3 forwardAxis,
                                      bool invertX,
                                      bool invertY,
                                      bool invertZ)
        {
            Vec3 dir = Vec3.Transform(forwardAxis, rotation);
            if (invertX) dir.X = -dir.X;
            if (invertY) dir.Y = -dir.Y;
            if (invertZ) dir.Z = -dir.Z;
            return Vec3.Normalize(dir);
        }

        private static Vec3 GetAxisVector(AxisModes axis) => axis switch
        {
            AxisModes.X => Vec3.UnitX,
            AxisModes.Y => Vec3.UnitY,
            AxisModes.Z => Vec3.UnitZ,
            _ => Vec3.Zero,
        };
        #endregion

        // IK LOGIC THAT WORKS FOR DMX (RESTORED/KEPT FROM PREVIOUS WORKING VERSION)
        private (float rawPan, float rawTilt) ComputePanTiltAngles(Vec3 direction,
                                                                  AxisModes panAxis,
                                                                  AxisModes tiltAxis,
                                                                  bool shouldLog)
        {
            Vec3 panVec = GetAxisVector(panAxis);
            Vec3 tiltVec = GetAxisVector(tiltAxis);
            float rawPan = 0f, rawTilt = 0f;

            // Handle cases where one or both axes are disabled
            if (panVec == Vec3.Zero && tiltVec == Vec3.Zero)
            {
                return (rawPan, rawTilt);
            }

            // If only one axis is enabled
            if (panVec == Vec3.Zero) // Only tilt enabled
            {
                // For tilt-only, we need to find the angle between the direction and the plane perpendicular to tilt axis
                Vec3 upVec = tiltVec;
                Vec3 forwardVec = FindOrthogonalVector(tiltVec);
                Vec3 rightVec = Vec3.Normalize(Vec3.Cross(upVec, forwardVec));

                // Project direction onto the plane defined by forward and right vectors
                float forwardComponent = Vec3.Dot(direction, forwardVec);
                float upComponent = Vec3.Dot(direction, upVec);

                rawTilt = MathF.Atan2(upComponent, forwardComponent);
                return (rawPan, rawTilt);
            }

            if (tiltVec == Vec3.Zero) // Only pan enabled
            {
                // For pan-only, rotate around pan axis
                Vec3 forwardVec = _cachedForwardAxis;
                Vec3 rightVec = Vec3.Normalize(Vec3.Cross(panVec, forwardVec));

                // Remove component along pan axis
                Vec3 directionInPlane = direction - panVec * Vec3.Dot(direction, panVec);
                if (directionInPlane.LengthSquared() > 1e-6f)
                {
                    directionInPlane = Vec3.Normalize(directionInPlane);
                    float rightComponent = Vec3.Dot(directionInPlane, rightVec);
                    float forwardComponent = Vec3.Dot(directionInPlane, forwardVec);
                    rawPan = MathF.Atan2(rightComponent, forwardComponent);
                }
                return (rawPan, rawTilt);
            }

            // Full 2-axis case - RESTORING ORIGINAL LOGIC
            // Create a coordinate system where:
            // - Z is the forward direction (cross product of tilt and pan axes)
            // - Y is the pan axis (rotation axis for pan)
            // - X is the tilt axis (rotation axis for tilt)

            Vec3 localForward = Vec3.Normalize(Vec3.Cross(tiltVec, panVec));
            if (localForward.LengthSquared() < 1e-6f)
            {
                Log.Error("Pan and Tilt axes are collinear – cannot form a proper coordinate system.", this);
                return (rawPan, rawTilt);
            }

            Vec3 localRight = Vec3.Normalize(Vec3.Cross(localForward, panVec));
            Vec3 localUp = Vec3.Normalize(Vec3.Cross(localRight, localForward));

            // Transform direction into local coordinate system
            float x = Vec3.Dot(direction, localRight);    // Tilt axis component
            float y = Vec3.Dot(direction, localUp);       // Pan axis component  
            float z = Vec3.Dot(direction, localForward);  // Forward component

            // Calculate pan angle (rotation around pan axis)
            rawPan = MathF.Atan2(x, z);

            // Calculate tilt angle (rotation around tilt axis)
            // Use atan2 for full range and better numerical stability
            float horizontalMagnitude = MathF.Sqrt(x * x + z * z);
            rawTilt = MathF.Atan2(y, horizontalMagnitude);

            if (shouldLog)
            {
                Log.Debug($"Local coords - X: {x:F3}, Y: {y:F3}, Z: {z:F3}", this);
                Log.Debug($"Calculated angles - Pan: {rawPan * 180f / MathF.PI:F1}°, Tilt: {rawTilt * 180f / MathF.PI:F1}°", this);
            }

            return (rawPan, rawTilt);
        }

        // Helper method to find an orthogonal vector
        private Vec3 FindOrthogonalVector(Vec3 vec)
        {
            // Try using unit X as candidate
            Vec3 candidate = Vec3.UnitX;
            if (Math.Abs(Vec3.Dot(vec, candidate)) > 0.9f)
            {
                // If too aligned, use unit Y
                candidate = Vec3.UnitY;
            }
            if (Math.Abs(Vec3.Dot(vec, candidate)) > 0.9f)
            {
                // If still too aligned, use unit Z
                candidate = Vec3.UnitZ;
            }

            // Make orthogonal using Gram-Schmidt
            return Vec3.Normalize(candidate - vec * Vec3.Dot(candidate, vec));
        }

        private float ApplyPanRangeAndWrite(EvaluationContext context,
                                            float rawPan,
                                            int panChannel,
                                            int panFineChannel,
                                            Vector2 panRangeDegrees,
                                            bool invertPan,
                                            bool useShortestPath,
                                            float lastPanValueRad,
                                            bool shouldLog)
        {
            if (panRangeDegrees.X >= panRangeDegrees.Y)
            {
                Log.Warning("Pan range min must be < max.", this);
                SetDmxValue(0f, panChannel, panFineChannel, 0f, 1f, shouldLog, "Pan");
                return 0f;
            }

            float panMinRad = panRangeDegrees.X * MathF.PI / 180f;
            float panMaxRad = panRangeDegrees.Y * MathF.PI / 180f;
            float panVal = rawPan;

            if (!useShortestPath || float.IsNaN(lastPanValueRad))
            {
                panVal = MathUtils.Fmod(panVal + MathF.PI, 2 * MathF.PI) - MathF.PI;
                if (shouldLog) Log.Debug($"Pan (normalized to -180/180): {panVal * 180f / MathF.PI:F2} deg", this);

                float rangeCenterRad = (panMinRad + panMaxRad) / 2f;
                float turnsToCenter = MathF.Round((panVal - rangeCenterRad) / (2 * MathF.PI));
                panVal -= turnsToCenter * 2 * MathF.PI;
                if (shouldLog) Log.Debug($"Pan (shifted to range center): {panVal * 180f / MathF.PI:F2} deg", this);
            }
            else
            {
                float turns = MathF.Round((lastPanValueRad - panVal) / (2 * MathF.PI));
                panVal += turns * 2 * MathF.PI;
                if (shouldLog) Log.Debug($"Pan (shortest path applied from last value {lastPanValueRad * 180f / MathF.PI:F2} deg): {panVal * 180f / MathF.PI:F2} deg", this);
            }

            if (invertPan)
            {
                panVal = panMaxRad + panMinRad - panVal;
                if (shouldLog) Log.Debug($"Pan (after inversion): {panVal * 180f / MathF.PI:F2} deg", this);
            }

            float finalPan = Math.Clamp(panVal, panMinRad, panMaxRad);
            if (shouldLog)
                Log.Debug($"Final Pan (clamped to {panRangeDegrees.X:F2}-{panRangeDegrees.Y:F2} deg): {finalPan * 180f / MathF.PI:F2} deg ({finalPan:F4} rad)", this);

            SetDmxValue(finalPan,
                        panChannel,
                        panFineChannel,
                        panMinRad,
                        panMaxRad,
                        shouldLog,
                        "Pan");

            if (useShortestPath)
                _lastPanTilt.X = finalPan;
            else
                _lastPanTilt.X = float.NaN;

            return finalPan;
        }

        private float ApplyTiltRangeAndWrite(EvaluationContext context,
                                             float rawTilt,
                                             int tiltChannel,
                                             int tiltFineChannel,
                                             Vector2 tiltRangeDegrees,
                                             bool invertTilt,
                                             bool useShortestPath,
                                             float lastTiltValueRad,
                                             bool shouldLog)
        {
            if (tiltRangeDegrees.X >= tiltRangeDegrees.Y)
            {
                Log.Warning("Tilt range min must be < max.", this);
                SetDmxValue(0f, tiltChannel, tiltFineChannel, 0f, 1f, shouldLog, "Tilt");
                return 0f;
            }

            float tiltMinRad = tiltRangeDegrees.X * MathF.PI / 180f;
            float tiltMaxRad = tiltRangeDegrees.Y * MathF.PI / 180f;
            float tiltVal = rawTilt;

            if (!useShortestPath || float.IsNaN(lastTiltValueRad))
            {
                tiltVal = MathUtils.Fmod(tiltVal + MathF.PI, 2 * MathF.PI) - MathF.PI;
                if (shouldLog) Log.Debug($"Tilt (normalized to -180/180): {tiltVal * 180f / MathF.PI:F2} deg", this);

                float rangeCenterRad = (tiltMinRad + tiltMaxRad) / 2f;
                float turnsToCenter = MathF.Round((tiltVal - rangeCenterRad) / (2 * MathF.PI));
                tiltVal -= turnsToCenter * 2 * MathF.PI;
                if (shouldLog) Log.Debug($"Tilt (shifted to range center): {tiltVal * 180f / MathF.PI:F2} deg", this);
            }
            else if (useShortestPath)
            {
                float turns = MathF.Round((lastTiltValueRad - tiltVal) / (2 * MathF.PI));
                tiltVal += turns * 2 * MathF.PI;
                if (shouldLog) Log.Debug($"Tilt (shortest path applied from last value {lastTiltValueRad * 180f / MathF.PI:F2} deg): {tiltVal * 180f / MathF.PI:F2} deg", this);
            }

            if (invertTilt)
            {
                tiltVal = tiltMaxRad + tiltMinRad - tiltVal;
                if (shouldLog) Log.Debug($"Tilt (after inversion): {tiltVal * 180f / MathF.PI:F2} deg", this);
            }

            float finalTilt = Math.Clamp(tiltVal, tiltMinRad, tiltMaxRad);
            if (shouldLog)
                Log.Debug($"Final Tilt (clamped to {tiltRangeDegrees.X:F2}-{tiltRangeDegrees.Y:F2} deg): {finalTilt * 180f / MathF.PI:F2} deg ({finalTilt:F4} rad)", this);

            SetDmxValue(finalTilt,
                        tiltChannel,
                        tiltFineChannel,
                        tiltMinRad,
                        tiltMaxRad,
                        shouldLog,
                        "Tilt");

            if (useShortestPath)
                _lastPanTilt.Y = finalTilt;
            else
                _lastPanTilt.Y = float.NaN;

            return finalTilt;
        }
        #endregion

        #region Colour / Feature Handling
        private void HandleColorAndFeatures(EvaluationContext context,
                                            Point[] points,
                                            Point transformPoint,
                                            int firstPixelIdx,
                                            int pixelsPerFixture)
        {
            bool getColor = GetColor.GetValue(context);
            bool getF1 = GetF1.GetValue(context);
            bool getF2 = GetF2.GetValue(context);
            bool f1ByPixel = GetF1ByPixel.GetValue(context);
            bool f2ByPixel = GetF2ByPixel.GetValue(context);

            bool useCMY = RGBToCMY.GetValue(context);
            bool is16BitColor = Is16BitColor.GetValue(context);
            int redChBase = RedChannel.GetValue(context);
            int greenChBase = GreenChannel.GetValue(context);
            int blueChBase = BlueChannel.GetValue(context);
            int whiteChBase = WhiteChannel.GetValue(context);
            int alphaChBase = AlphaChannel.GetValue(context);
            int f1ChBase = F1Channel.GetValue(context);
            int f2ChBase = F2Channel.GetValue(context);

            bool hasAnyPerPixelAttributes = (getColor && (redChBase > 0 || greenChBase > 0 || blueChBase > 0 || whiteChBase > 0 || alphaChBase > 0)) ||
                                            (getF1 && f1ByPixel && f1ChBase > 0) ||
                                            (getF2 && f2ByPixel && f2ChBase > 0);

            int overallMinPerPixelChannel = int.MaxValue;
            int overallMaxPerPixelChannel = int.MinValue;

            if (hasAnyPerPixelAttributes)
            {
                int channelsPerColorDmxValue = is16BitColor ? 2 : 1;
                int channelsPerFeatureDmxValue = 1;

                if (getColor)
                {
                    if (redChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, redChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, redChBase + channelsPerColorDmxValue - 1); }
                    if (greenChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, greenChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, greenChBase + channelsPerColorDmxValue - 1); }
                    if (blueChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, blueChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, blueChBase + channelsPerColorDmxValue - 1); }
                    if (whiteChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, whiteChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, whiteChBase + channelsPerColorDmxValue - 1); }
                    if (alphaChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, alphaChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, alphaChBase + channelsPerColorDmxValue - 1); }
                }
                if (getF1 && f1ByPixel && f1ChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, f1ChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, f1ChBase + channelsPerFeatureDmxValue - 1); }
                if (getF2 && f2ByPixel && f2ChBase > 0) { overallMinPerPixelChannel = Math.Min(overallMinPerPixelChannel, f2ChBase); overallMaxPerPixelChannel = Math.Max(overallMaxPerPixelChannel, f2ChBase + channelsPerFeatureDmxValue - 1); }
            }

            int pixelChannelStride = 0;
            if (overallMinPerPixelChannel != int.MaxValue && overallMaxPerPixelChannel != int.MinValue)
            {
                pixelChannelStride = overallMaxPerPixelChannel - overallMinPerPixelChannel + 1;
                if (DebugToLog.GetValue(context)) Log.Debug($"Calculated pixelChannelStride: {pixelChannelStride} (min: {overallMinPerPixelChannel}, max: {overallMaxPerPixelChannel})", this);
            }
            else if (hasAnyPerPixelAttributes && DebugToLog.GetValue(context))
            {
                Log.Warning("Per-pixel attributes are enabled, but all associated DMX channels are 0 or less. No per-pixel output will be generated.", this);
            }

            for (int pix = 0; pix < pixelsPerFixture; ++pix)
            {
                Point pt = points[firstPixelIdx + pix];

                void WriteSinglePixelDmxValue(int baseChannel, float normalizedValue, bool is16BitChannel, string debugName)
                {
                    if (baseChannel <= 0) return;
                    if (pixelChannelStride == 0) return;

                    int relativeOffsetFromOverallMin = baseChannel - overallMinPerPixelChannel;
                    int actualDmxCoarseChannel = overallMinPerPixelChannel + (pix * pixelChannelStride) + relativeOffsetFromOverallMin;

                    int actualDmxFineChannel = is16BitChannel ? actualDmxCoarseChannel + 1 : 0;

                    SetDmxValue(normalizedValue,
                                actualDmxCoarseChannel,
                                actualDmxFineChannel,
                                0f, 1f,
                                false,
                                $"Pixel{pix}-{debugName}");
                }

                if (getColor)
                {
                    if (redChBase > 0 || greenChBase > 0 || blueChBase > 0 || whiteChBase > 0 || alphaChBase > 0)
                    {
                        float r = float.IsNaN(pt.Color.X) ? 0f : Math.Clamp(pt.Color.X, 0f, 1f);
                        float g = float.IsNaN(pt.Color.Y) ? 0f : Math.Clamp(pt.Color.Y, 0f, 1f);
                        float b = float.IsNaN(pt.Color.Z) ? 0f : Math.Clamp(pt.Color.Z, 0f, 1f);
                        float a = float.IsNaN(pt.Color.W) ? 1f : Math.Clamp(pt.Color.W, 0f, 1f);

                        if (useCMY) { r = 1f - r; g = 1f - g; b = 1f - b; }

                        WriteSinglePixelDmxValue(redChBase, r, is16BitColor, "Red");
                        if (greenChBase > 0) WriteSinglePixelDmxValue(greenChBase, g, is16BitColor, "Green");
                        if (blueChBase > 0) WriteSinglePixelDmxValue(blueChBase, b, is16BitColor, "Blue");
                        if (whiteChBase > 0)
                        {
                            float w = Math.Min(r, Math.Min(g, b));
                            WriteSinglePixelDmxValue(whiteChBase, w, is16BitColor, "White");
                        }
                        if (alphaChBase > 0)
                        {
                            WriteSinglePixelDmxValue(alphaChBase, a, is16BitColor, "Alpha");
                        }
                    }
                }

                if (getF1 && f1ByPixel && f1ChBase > 0)
                {
                    float f1 = float.IsNaN(pt.F1) ? 0f : Math.Clamp(pt.F1, 0f, 1f);
                    WriteSinglePixelDmxValue(f1ChBase, f1, false, "F1");
                }

                if (getF2 && f2ByPixel && f2ChBase > 0)
                {
                    float f2 = float.IsNaN(pt.F2) ? 0f : Math.Clamp(pt.F2, 0f, 1f);
                    WriteSinglePixelDmxValue(f2ChBase, f2, false, "F2");
                }
            }

            // Fixture‑wide (non‑per‑pixel) F1/F2
            if (getF1 && !f1ByPixel && f1ChBase > 0)
            {
                float f1Val = float.IsNaN(transformPoint.F1) ? 0f : Math.Clamp(transformPoint.F1, 0f, 1f);
                SetDmxValue(f1Val, f1ChBase, 0, 0f, 1f, DebugToLog.GetValue(context), "FixtureF1");
            }

            if (getF2 && !f2ByPixel && f2ChBase > 0)
            {
                float f2Val = float.IsNaN(transformPoint.F2) ? 0f : Math.Clamp(transformPoint.F2, 0f, 1f);
                SetDmxValue(f2Val, f2ChBase, 0, 0f, 1f, DebugToLog.GetValue(context), "FixtureF2");
            }
        }
        #endregion

        #region Custom Variable Handling
        private void HandleCustomVariables(EvaluationContext ctx)
        {
            const float CustomVarNormalizedMax = 255f;
            bool shouldLog = DebugToLog.GetValue(ctx);

            if (SetCustomVar1.GetValue(ctx) && CustomVar1Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar1.GetValue(ctx), 0, (int)CustomVarNormalizedMax);
                SetDmxValue(value, CustomVar1Channel.GetValue(ctx), 0, 0f, CustomVarNormalizedMax, shouldLog, "CustomVar1");
            }
            if (SetCustomVar2.GetValue(ctx) && CustomVar2Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar2.GetValue(ctx), 0, (int)CustomVarNormalizedMax);
                SetDmxValue(value, CustomVar2Channel.GetValue(ctx), 0, 0f, CustomVarNormalizedMax, shouldLog, "CustomVar2");
            }
            if (SetCustomVar3.GetValue(ctx) && CustomVar3Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar3.GetValue(ctx), 0, (int)CustomVarNormalizedMax);
                SetDmxValue(value, CustomVar3Channel.GetValue(ctx), 0, 0f, CustomVarNormalizedMax, shouldLog, "CustomVar3");
            }
            if (SetCustomVar4.GetValue(ctx) && CustomVar4Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar4.GetValue(ctx), 0, (int)CustomVarNormalizedMax);
                SetDmxValue(value, CustomVar4Channel.GetValue(ctx), 0, 0f, CustomVarNormalizedMax, shouldLog, "CustomVar4");
            }
            if (SetCustomVar5.GetValue(ctx) && CustomVar5Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar5.GetValue(ctx), 0, (int)CustomVarNormalizedMax);
                SetDmxValue(value, CustomVar5Channel.GetValue(ctx), 0, 0f, CustomVarNormalizedMax, shouldLog, "CustomVar5");
            }
        }
        #endregion

        #region DMX Helper Methods
        private void SetDmxValue(float value,
                                 int coarseChannel,
                                 int fineChannel,
                                 float inMin,
                                 float inMax,
                                 bool shouldLog,
                                 string name)
        {
            if (coarseChannel <= 0)
            {
                if (shouldLog) Log.Debug($"Skipping DMX write for {name}: Coarse Channel is 0 or less.", this);
                return;
            }

            int listCoarseIndex = coarseChannel - 1;

            if (fineChannel > 0)
            {
                int dmx16 = MapToDmx16(value, inMin, inMax);

                if (shouldLog)
                    Log.Debug($"{name} DMX Channel: {coarseChannel}/{fineChannel} (16-bit), Input Value: {value:F4}, Mapped DMX (16‑bit): {dmx16}, Range: [{inMin:F4}, {inMax:F4}]", this);

                InsertOrSet(listCoarseIndex, (dmx16 >> 8) & 0xFF);
                InsertOrSet(fineChannel - 1, dmx16 & 0xFF);
            }
            else
            {
                float range = inMax - inMin;
                float normalized = Math.Clamp((value - inMin) / (range), 0f, 1f);
                int dmx8 = (int)Math.Round(normalized * 255.0f);

                if (shouldLog)
                    Log.Debug($"{name} DMX Channel: {coarseChannel} (8-bit), Input Value: {value:F4}, Mapped DMX (8‑bit): {dmx8}, Range: [{inMin:F4}, {inMax:F4}]", this);

                InsertOrSet(listCoarseIndex, dmx8);
            }
        }

        private static int MapToDmx16(float value, float inMin, float inMax)
        {
            float range = inMax - inMin;
            if (Math.Abs(range) < 1e-4f) return 0;
            float normalized = (value - inMin) / range;
            return (int)Math.Round((double)(Math.Clamp(normalized, 0f, 1f) * 65535f));
        }

        private bool InsertOrSet(int index, int value)
        {
            if (index < 0) return false;
            if (index >= _pointChannelValues.Count)
            {
                Log.Warning($"DMX channel list index {index + 1} out of range (list size {_pointChannelValues.Count}). " +
                            $"Increase 'Fixture Channel Size' if you are using high channel numbers or 16-bit channels for multiple pixels.", this);
                return false;
            }
            _pointChannelValues[index] = value;
            return true;
        }
        #endregion

        #region Visualisation Buffer
        private void UpdateVisualizationBuffer()
        {
            if (_visualizationPoints == null || _visualizationPoints.Length == 0)
            {
                _visualizeBuffer = null;
                return;
            }

            int pointCount = _visualizationPoints.Length;
            int stride = Point.Stride;

            Buffer buffer = null;
            ShaderResourceView srv = null;
            UnorderedAccessView uav = null;

            if (_visualizeBuffer != null)
            {
                buffer = _visualizeBuffer.Buffer;
                srv = _visualizeBuffer.Srv;
                uav = _visualizeBuffer.Uav;
            }

            ResourceManager.SetupStructuredBuffer(_visualizationPoints,
                                                 stride * pointCount,
                                                 stride,
                                                 ref buffer);
            ResourceManager.CreateStructuredBufferSrv(buffer, ref srv);
            ResourceManager.CreateStructuredBufferUav(buffer,
                                                     UnorderedAccessViewBufferFlags.None,
                                                     ref uav);

            if (_visualizeBuffer == null)
                _visualizeBuffer = new BufferWithViews();

            _visualizeBuffer.Buffer = buffer;
            _visualizeBuffer.Srv = srv;
            _visualizeBuffer.Uav = uav;
        }
        #endregion
    }
}