using SharpDX;
using T3.Core.Utils;

namespace Lib.io.dmx
{
    // ------------------------------------------------------------------------
    //  Type aliases – keep System.Numerics types distinct from SharpDX types.
    // ------------------------------------------------------------------------
    using Vec3 = Vector3;
    using Quat = Quaternion;

    [Guid("c9d7cd19-7fc6-4491-8dfa-3808725c7857")]
    public sealed class PointsToDmxLights : Instance<PointsToDmxLights>
    {
        #region Output Slots
        [Output(Guid = "8DC2DB32-D7A3-4B3A-A000-93C3107D19E4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<int>> Result = new(new List<int>(20));

        [Output(Guid = "da7deb8c-4218-4cae-9ec5-fd7c2e6f4c35")]
        public readonly Slot<BufferWithViews> VisualizeLights = new();
        #endregion

        #region Enums
        public enum AxisModes { Disabled, X, Y, Z }
        public enum RotationOrderModes { PanThenTilt, TiltThenPan }
        public enum ForwardVectorModes { X, Y, Z, NegX, NegY, NegZ }

        public enum TestMode
        {
            Disabled = 0,
            ZPositive,
            ZNegative,
            XPositive,
            XNegative,
            YPositive,
            YNegative
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
        public PointsToDmxLights()
        {
            Result.UpdateAction = Update;
        }
        #endregion

        #region Input Slots
        // Buffers
        [Input(Guid = "61b48e46-c3d1-46e3-a470-810d55f30aa6")]
        public readonly InputSlot<BufferWithViews> EffectedPoints = new();

        [Input(Guid = "2bea2ccb-89f2-427b-bd9a-95c7038b715e")]
        public readonly InputSlot<BufferWithViews> ReferencePoints = new();

        // General behaviour
        [Input(Guid = "1348ed7c-79f8-48c6-ac00-e60fb40050db")]
        public readonly InputSlot<int> FixtureChannelSize = new();

        [Input(Guid = "7449cd05-54be-484b-854a-d2143340f925")]
        public readonly InputSlot<bool> FitInUniverse = new();

        [Input(Guid = "850af6c3-d9ef-492c-9cfb-e2589ae5b9ac")]
        public readonly InputSlot<bool> FillUniverse = new();

        [Input(Guid = "23F23213-68E2-45F5-B452-4A86289004C0")]
        public readonly InputSlot<bool> DebugToLog = new();

        // Test‑Mode dropdown (debug only)
        [Input(Guid = "D8A90C30-4E5B-4F0B-BFA7-09DAF3A4C71F", MappedType = typeof(TestMode))]
        public readonly InputSlot<int> TestModeSelect = new();

        // POSITION
        [Input(Guid = "df04fce0-c6e5-4039-b03f-e651fc0ec4a9")]
        public readonly InputSlot<bool> GetPosition = new();

        [Input(Guid = "628d96a8-466b-4148-9658-7786833ec989", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> PositionMeasureAxis = new();

        [Input(Guid = "78a7e683-f4e7-4826-8e39-c8de08e50e5e")]
        public readonly InputSlot<bool> InvertPositionDirection = new();

        [Input(Guid = "8880c101-403f-46e0-901e-20ec2dd333e9")]
        public readonly InputSlot<Vector2> PositionDistanceRange = new();

        [Input(Guid = "fc3ec0d6-8567-4d5f-9a63-5c69fb5988cb")]
        public readonly InputSlot<int> PositionChannel = new();

        [Input(Guid = "658a19df-e51b-45b4-9f91-cb97a891255a")]
        public readonly InputSlot<int> PositionFineChannel = new();

        // ROTATION
        [Input(Guid = "4922acd8-ab83-4394-8118-c555385c2ce9")]
        public readonly InputSlot<bool> GetRotation = new();

        [Input(Guid = "032F3617-E1F3-4B41-A3BE-61DD63B9F3BA", MappedType = typeof(ForwardVectorModes))]
        public readonly InputSlot<int> ForwardVector = new();

        [Input(Guid = "9c235473-346b-4861-9844-4b584e09f58a", MappedType = typeof(RotationOrderModes))]
        public readonly InputSlot<int> RotationOrder = new();

        [Input(Guid = "49fefbdb-2652-43db-ae52-ebc2df3e2856")]
        public readonly InputSlot<bool> InvertX = new();

        [Input(Guid = "6d8fc457-0c80-4736-8c25-cc48f07cbbfd")]
        public readonly InputSlot<bool> InvertY = new();

        [Input(Guid = "0c57cdd5-e450-4425-954f-c9e4256f83e1")]
        public readonly InputSlot<bool> InvertZ = new();

        [Input(Guid = "1f532994-fb0e-44e4-8a80-7917e1851eae", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> PanAxis = new();

        [Input(Guid = "7bf3e057-b9eb-43d2-8e1a-64c1c3857ca1")]
        public readonly InputSlot<bool> InvertPan = new();

        [Input(Guid = "1f877cf6-10d9-4d0b-b087-974bd6855e0a", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> TiltAxis = new();

        [Input(Guid = "f85ecf9f-0c3d-4c10-8ba7-480aa2c7a667")]
        public readonly InputSlot<bool> InvertTilt = new();

        [Input(Guid = "e96655be-6bc7-4ca4-bf74-079a07570d74")]
        public readonly InputSlot<bool> ShortestPathPanTilt = new();

        [Input(Guid = "f50da250-606d-4a15-a25e-5458f540e527")]
        public readonly InputSlot<Vector2> PanRange = new();

        [Input(Guid = "9000c279-73e4-4de8-a1f8-c3914eaaf533")]
        public readonly InputSlot<int> PanChannel = new();

        [Input(Guid = "4d4b3425-e6ad-4834-a8a7-06c9f9c2b909")]
        public readonly InputSlot<int> PanFineChannel = new();

        [Input(Guid = "6e8b4125-0e8c-430b-897d-2231bb4c8f6f")]
        public readonly InputSlot<Vector2> TiltRange = new();

        [Input(Guid = "47d7294f-6f73-4e21-ac9a-0fc0817283fb")]
        public readonly InputSlot<int> TiltChannel = new();

        [Input(Guid = "4a40e022-d206-447c-bda3-d534f231c816")]
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
        [Input(Guid = "5cdc69f7-45ec-4eec-bfb6-960d6245dafb")]
        public readonly InputSlot<bool> GetColor = new();

        [Input(Guid = "cf2c3308-8f3f-442d-a563-b419f12e7ad1")]
        public readonly InputSlot<bool> RgbToCmy = new();

        [Input(Guid = "013cc355-91d6-4ea6-b9f7-f1817b89e4a3")]
        public readonly InputSlot<int> RedChannel = new();

        [Input(Guid = "970769f4-116f-418d-87a7-cda28e44d063")]
        public readonly InputSlot<int> GreenChannel = new();

        [Input(Guid = "d755342b-9a9e-4c78-8376-81579d8c0909")]
        public readonly InputSlot<int> BlueChannel = new();

        [Input(Guid = "f13edebd-b44f-49e9-985e-7e3feb886fea")]
        public readonly InputSlot<int> AlphaChannel = new();

        [Input(Guid = "8ceece78-9a08-4c7b-8fea-740e8e5929a6")]
        public readonly InputSlot<int> WhiteChannel = new();

        [Input(Guid = "5E96A7A3-5340-43F2-96B9-9972A69421E5")]
        public readonly InputSlot<bool> Is16BitColor = new();

        // FEATURES (F1 / F2)
        [Input(Guid = "91c78090-be10-4203-827e-d2ef1b93317e")]
        public readonly InputSlot<bool> GetF1 = new();

        [Input(Guid = "bec9e5a6-40a9-49b2-88bd-01a4ea03d28c")]
        public readonly InputSlot<bool> GetF1ByPixel = new();

        [Input(Guid = "b7061834-66aa-4f7f-91f9-10ebfe16713f")]
        public readonly InputSlot<int> F1Channel = new();

        [Input(Guid = "1cb93e97-0161-4a77-bbc7-ff30c1972cf8")]
        public readonly InputSlot<bool> GetF2 = new();

        [Input(Guid = "b8080f4e-4542-4e20-9844-8028bbaf223f")]
        public readonly InputSlot<bool> GetF2ByPixel = new();

        [Input(Guid = "d77be0d1-5fb9-4d26-9e4a-e16497e4759c")]
        public readonly InputSlot<int> F2Channel = new();

        // CUSTOM VARIABLES
        [Input(Guid = "25e5f0ce-5ec8-4c99-beb1-317c6911a128")]
        public readonly InputSlot<bool> SetCustomVar1 = new();

        [Input(Guid = "b08c920f-0d6b-4820-bc2d-81a47d5f1147")]
        public readonly InputSlot<int> CustomVar1Channel = new();

        [Input(Guid = "50e849e8-5582-432e-98f7-d8e036273864")]
        public readonly InputSlot<int> CustomVar1 = new();

        [Input(Guid = "18cc3a73-3a1a-4370-87b7-e5cd44f4a3ab")]
        public readonly InputSlot<bool> SetCustomVar2 = new();

        [Input(Guid = "098f1662-6f47-4dd0-9a73-4c4814aefb23")]
        public readonly InputSlot<int> CustomVar2Channel = new();

        [Input(Guid = "e7a48fe0-d788-4f12-a9d4-52472519da09")]
        public readonly InputSlot<int> CustomVar2 = new();

        [Input(Guid = "876ef5b5-f2c6-4501-9e55-00b9a553a2e3")]
        public readonly InputSlot<bool> SetCustomVar3 = new();

        [Input(Guid = "ac9a709e-6dc0-40ca-9f70-350e655a2630")]
        public readonly InputSlot<int> CustomVar3Channel = new();

        [Input(Guid = "d16d7c5c-2795-4fde-85fd-13b515191fbe")]
        public readonly InputSlot<int> CustomVar3 = new();

        [Input(Guid = "8dd3fc1c-cd94-4bf0-b948-d6f734916d49")]
        public readonly InputSlot<bool> SetCustomVar4 = new();

        [Input(Guid = "cbaf821c-0305-4c74-a632-864081cc9a34")]
        public readonly InputSlot<int> CustomVar4Channel = new();

        [Input(Guid = "b29ebe11-89cb-4f86-aee0-cf729fa0d62c")]
        public readonly InputSlot<int> CustomVar4 = new();

        [Input(Guid = "a9315f88-6024-42e9-9691-4544627f0bef")]
        public readonly InputSlot<bool> SetCustomVar5 = new();

        [Input(Guid = "7c59a5fb-052a-443c-9e10-cf859fe25658")]
        public readonly InputSlot<int> CustomVar5Channel = new();

        [Input(Guid = "58cc3eee-e81e-4bab-b12c-e7bc3cf62dd0")]
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
                case TestMode.ZPositive:
                    _cachedForwardAxis = Vec3.UnitZ;
                    break;
                case TestMode.ZNegative:
                    _cachedForwardAxis = -Vec3.UnitZ;
                    break;
                case TestMode.XPositive:
                    _cachedForwardAxis = Vec3.UnitX;
                    break;
                case TestMode.XNegative:
                    _cachedForwardAxis = -Vec3.UnitX;
                    break;
                case TestMode.YPositive:
                    _cachedForwardAxis = Vec3.UnitY;
                    break;
                case TestMode.YNegative:
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
                finalPan = ApplyPanRangeAndWrite(rawPan,
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
                finalTilt = ApplyTiltRangeAndWrite(rawTilt,
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
                Vec3.Normalize(Vec3.Cross(upVec, forwardVec));

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

        private float ApplyPanRangeAndWrite(float rawPan,
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

        private float ApplyTiltRangeAndWrite(float rawTilt,
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
            else
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

            bool useCmy = RgbToCmy.GetValue(context);
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

                        if (useCmy) { r = 1f - r; g = 1f - g; b = 1f - b; }

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
            const float customVarNormalizedMax = 255f;
            bool shouldLog = DebugToLog.GetValue(ctx);

            if (SetCustomVar1.GetValue(ctx) && CustomVar1Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar1.GetValue(ctx), 0, (int)customVarNormalizedMax);
                SetDmxValue(value, CustomVar1Channel.GetValue(ctx), 0, 0f, customVarNormalizedMax, shouldLog, "CustomVar1");
            }
            if (SetCustomVar2.GetValue(ctx) && CustomVar2Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar2.GetValue(ctx), 0, (int)customVarNormalizedMax);
                SetDmxValue(value, CustomVar2Channel.GetValue(ctx), 0, 0f, customVarNormalizedMax, shouldLog, "CustomVar2");
            }
            if (SetCustomVar3.GetValue(ctx) && CustomVar3Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar3.GetValue(ctx), 0, (int)customVarNormalizedMax);
                SetDmxValue(value, CustomVar3Channel.GetValue(ctx), 0, 0f, customVarNormalizedMax, shouldLog, "CustomVar3");
            }
            if (SetCustomVar4.GetValue(ctx) && CustomVar4Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar4.GetValue(ctx), 0, (int)customVarNormalizedMax);
                SetDmxValue(value, CustomVar4Channel.GetValue(ctx), 0, 0f, customVarNormalizedMax, shouldLog, "CustomVar4");
            }
            if (SetCustomVar5.GetValue(ctx) && CustomVar5Channel.GetValue(ctx) > 0)
            {
                float value = Math.Clamp(CustomVar5.GetValue(ctx), 0, (int)customVarNormalizedMax);
                SetDmxValue(value, CustomVar5Channel.GetValue(ctx), 0, 0f, customVarNormalizedMax, shouldLog, "CustomVar5");
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
            return (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 65535f);
        }

        private void InsertOrSet(int index, int value)
        {
            if (index < 0)
                return;
            if (index >= _pointChannelValues.Count)
            {
                Log.Warning($"DMX channel list index {index + 1} out of range (list size {_pointChannelValues.Count}). " +
                            $"Increase 'Fixture Channel Size' if you are using high channel numbers or 16-bit channels for multiple pixels.", this);
                return;
            }
            _pointChannelValues[index] = value;
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