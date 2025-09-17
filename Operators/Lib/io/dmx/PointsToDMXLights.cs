using SharpDX;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.dmx
{
    [Guid("c9d7cd19-7fc6-4491-8dfa-3808725c7857")]
    public sealed class PointsToDMXLights : Instance<PointsToDMXLights>
    {
        [Output(Guid = "8DC2DB32-D7A3-4B3A-A000-93C3107D19E4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<int>> Result = new(new List<int>(20));

        public PointsToDMXLights()
        {
            Result.UpdateAction = Update;
        }

        private readonly List<int> _resultItems = [128];
        private const int UniverseSize = 512;

        private void Update(EvaluationContext context)
        {
            var pointBuffer = EffectedPoints.GetValue(context);
            var referencePointBuffer = ReferencePoints.GetValue(context);

            if (pointBuffer == null)
            {
                Log.Warning("EffectedPoints buffer is not connected.", this);
                Result.Value.Clear();
                return;
            }

            _pointsBufferReader.InitiateRead(pointBuffer.Buffer,
                                             pointBuffer.Srv.Description.Buffer.ElementCount,
                                             pointBuffer.Buffer.Description.StructureByteStride,
                                             OnPointsReadComplete);
            _pointsBufferReader.Update();

            if (referencePointBuffer != null)
            {
                _referencePointsBufferReader.InitiateRead(referencePointBuffer.Buffer,
                                                         referencePointBuffer.Srv.Description.Buffer.ElementCount,
                                                         referencePointBuffer.Buffer.Description.StructureByteStride,
                                                         OnReferencePointsReadComplete);
                _referencePointsBufferReader.Update();
            }
            else
            {
                if (_referencePoints.Length > 0)
                    _referencePoints = [];
            }

            if (_points != null && _points.Length > 0)
            {
                UpdateChannelData(context, _points);
                Result.Value = _resultItems;
            }
            else
            {
                Result.Value.Clear();
            }
        }

        private void OnPointsReadComplete(StructuredBufferReadAccess.ReadRequestItem readItem, IntPtr dataPointer, DataStream dataStream)
        {
            int count = readItem.ElementCount;
            if (_points.Length != count)
                _points = new Point[count];
            using (dataStream) { dataStream.ReadRange(_points, 0, count); }
        }

        private void OnReferencePointsReadComplete(StructuredBufferReadAccess.ReadRequestItem readItem, IntPtr dataPointer, DataStream dataStream)
        {
            int count = readItem.ElementCount;
            if (_referencePoints.Length != count)
                _referencePoints = new Point[count];
            using (dataStream) { dataStream.ReadRange(_referencePoints, 0, count); }
        }

        private void UpdateChannelData(EvaluationContext context, Point[] points)
        {
            var fixtureChannelSize = FixtureChannelSize.GetValue(context);
            var effectedPointsCount = points.Length;

            int fixtureCount;
            int pixelsPerFixture;

            bool useReferencePoints = _referencePoints.Length > 0;

            if (useReferencePoints)
            {
                fixtureCount = _referencePoints.Length;
                if (fixtureCount == 0 || effectedPointsCount % fixtureCount != 0)
                {
                    Log.Warning($"Effected points count ({effectedPointsCount}) is not a multiple of reference points count ({fixtureCount}). Falling back to 1-to-1 mapping.", this);
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

            var fitInUniverse = FitInUniverse.GetValue(context);
            var fillUniverse = FillUniverse.GetValue(context);

            _resultItems.Clear();
            _pointChannelValues.Clear();

            if (fixtureChannelSize > 0)
            {
                for (var i = 0; i < fixtureChannelSize; i++) _pointChannelValues.Add(0);
            }
            else
            {
                return;
            }

            // Get channel settings once
            var redCh = RedChannel.GetValue(context);
            var greenCh = GreenChannel.GetValue(context);
            var blueCh = BlueChannel.GetValue(context);
            var whiteCh = WhiteChannel.GetValue(context);
            var alphaCh = AlphaChannel.GetValue(context);

            for (var fixtureIndex = 0; fixtureIndex < fixtureCount; fixtureIndex++)
            {
                for (var i = 0; i < fixtureChannelSize; i++) { _pointChannelValues[i] = 0; }

                var firstPointIndexForFixture = fixtureIndex * pixelsPerFixture;
                var transformPoint = points[firstPointIndexForFixture];

                Point referencePoint = useReferencePoints ? _referencePoints[fixtureIndex] : transformPoint;

                if (GetRotation.GetValue(context)) ProcessRotation(context, transformPoint, referencePoint, useReferencePoints);
                if (GetPosition.GetValue(context)) ProcessPosition(context, transformPoint, referencePoint, useReferencePoints);

                // --- NEW DYNAMIC COLOR PROCESSING ---
                if (GetColor.GetValue(context) && redCh > 0)
                {
                    var currentDmxChannelIndex = redCh - 1;
                    var useCmy = RGBToCMY.GetValue(context);

                    for (var pixelIndex = 0; pixelIndex < pixelsPerFixture; pixelIndex++)
                    {
                        var pointForColor = points[firstPointIndexForFixture + pixelIndex];

                        // Calculate all potential color values
                        float r = float.IsNaN(pointForColor.Color.X) ? 0f : Math.Clamp(pointForColor.Color.X, 0f, 1f);
                        float g = float.IsNaN(pointForColor.Color.Y) ? 0f : Math.Clamp(pointForColor.Color.Y, 0f, 1f);
                        float b = float.IsNaN(pointForColor.Color.Z) ? 0f : Math.Clamp(pointForColor.Color.Z, 0f, 1f);

                        if (useCmy) { r = 1f - r; g = 1f - g; b = 1f - b; }

                        var vR = r * 255.0f;
                        var vG = g * 255.0f;
                        var vB = b * 255.0f;
                        var vW = Math.Min(vR, Math.Min(vG, vB));
                        var vA = pointForColor.Color.W * 255f;

                        // Write enabled channels sequentially
                        if (redCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vR));
                        if (greenCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vG));
                        if (blueCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vB));
                        if (whiteCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vW));
                        if (alphaCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vA));
                    }
                }

                if (GetF1.GetValue(context)) ProcessF1(context, transformPoint);
                if (GetF2.GetValue(context)) ProcessF2(context, transformPoint);

                // Custom Variables...
                if (SetCustomVar1.GetValue(context) && CustomVar1Channel.GetValue(context) > 0) InsertOrSet(CustomVar1Channel.GetValue(context) - 1, CustomVar1.GetValue(context));
                if (SetCustomVar2.GetValue(context) && CustomVar2Channel.GetValue(context) > 0) InsertOrSet(CustomVar2Channel.GetValue(context) - 1, CustomVar2.GetValue(context));
                if (SetCustomVar3.GetValue(context) && CustomVar3Channel.GetValue(context) > 0) InsertOrSet(CustomVar3Channel.GetValue(context) - 1, CustomVar3.GetValue(context));
                if (SetCustomVar4.GetValue(context) && CustomVar4Channel.GetValue(context) > 0) InsertOrSet(CustomVar4Channel.GetValue(context) - 1, CustomVar4.GetValue(context));
                if (SetCustomVar5.GetValue(context) && CustomVar5Channel.GetValue(context) > 0) InsertOrSet(CustomVar5Channel.GetValue(context) - 1, CustomVar5.GetValue(context));

                if (fitInUniverse)
                {
                    var currentChannelIndex = _resultItems.Count;
                    var remainingInUniverse = UniverseSize - (currentChannelIndex % UniverseSize);
                    if (fixtureChannelSize > remainingInUniverse)
                    {
                        for (var i = 0; i < remainingInUniverse; i++) { _resultItems.Add(0); }
                    }
                }
                _resultItems.AddRange(_pointChannelValues);
            }

            if (fillUniverse)
            {
                var currentSize = _resultItems.Count;
                var remainder = currentSize % UniverseSize;
                if (remainder != 0)
                {
                    var toAdd = UniverseSize - remainder;
                    for (var i = 0; i < toAdd; i++) { _resultItems.Add(0); }
                }
            }
        }


        private Vector2 _lastPanTilt = new(float.NaN, float.NaN);
        private Point[] _points = [];
        private Point[] _referencePoints = [];

        private void ProcessRotation(EvaluationContext context, Point point, Point referencePoint, bool calculateRelativeRotation)
        {
            var rotation = point.Orientation;
            if (float.IsNaN(rotation.X) || float.IsNaN(rotation.Y) || float.IsNaN(rotation.Z) || float.IsNaN(rotation.W))
                return;

            var axisOrder = (RotationModes)AxisOrder.GetValue(context);
            var initialForwardAxis = Vector3.UnitZ;

            Quaternion activeRotation;
            if (calculateRelativeRotation)
            {
                var refRotation = referencePoint.Orientation;
                if (float.IsNaN(refRotation.X) || float.IsNaN(refRotation.Y) || float.IsNaN(refRotation.Z) || float.IsNaN(refRotation.W))
                {
                    activeRotation = rotation;
                }
                else
                {
                    activeRotation = Quaternion.Inverse(refRotation) * rotation;
                }
            }
            else
            {
                activeRotation = rotation;
            }

            var direction = Vector3.Transform(initialForwardAxis, activeRotation);
            direction = Vector3.Normalize(direction);

            float panValue, tiltValue;
            var calculationMode = axisOrder == RotationModes.ForReferencePoints ? RotationModes.YXZ : axisOrder;

            switch (calculationMode)
            {
                case RotationModes.YXZ: panValue = MathF.Atan2(direction.X, direction.Z); tiltValue = MathF.Asin(direction.Y); break;
                case RotationModes.YZX: panValue = MathF.Atan2(direction.Z, direction.X); tiltValue = MathF.Atan2(direction.Y, MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z)); break;
                case RotationModes.ZYX: panValue = MathF.Atan2(direction.Y, direction.X); tiltValue = MathF.Asin(-direction.Z); break;
                case RotationModes.ZXY: panValue = MathF.Atan2(direction.Y, direction.X); tiltValue = -MathF.Atan2(direction.Z, MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y)); break;
                case RotationModes.XZY: panValue = MathF.Atan2(direction.Z, direction.Y); tiltValue = MathF.Asin(-direction.X); break;
                case RotationModes.XYZ: panValue = MathF.Atan2(-direction.Y, direction.Z); tiltValue = MathF.Asin(direction.X); break;
                case RotationModes.LegacyZ: panValue = MathF.Atan2(direction.X, direction.Y); tiltValue = MathF.Atan2(direction.Z, MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y)) - MathF.PI / 2; break;
                default: panValue = MathF.Atan2(direction.X, direction.Z); tiltValue = MathF.Asin(direction.Y); break;
            }

            var panRange = PanRange.GetValue(context);
            var tiltRange = TiltRange.GetValue(context);

            if (panRange.X >= panRange.Y || tiltRange.X >= tiltRange.Y)
            {
                Log.Warning("Min range value must be less than max range value for Pan/Tilt.", this);
                return;
            }

            var panMin = panRange.X * MathF.PI / 180f;
            var panMax = panRange.Y * MathF.PI / 180f;
            var tiltMin = tiltRange.X * MathF.PI / 180f;
            var tiltMax = tiltRange.Y * MathF.PI / 180f;

            if (ShortestPathPanTilt.GetValue(context) && !float.IsNaN(_lastPanTilt.X))
            {
                var prevPan = _lastPanTilt.X;
                var panSpan = panMax - panMin;
                var unwrappedPan = panValue;

                if (panSpan > MathF.PI * 1.5f)
                {
                    var turns = MathF.Round((prevPan - panValue) / (2 * MathF.PI));
                    unwrappedPan = panValue + turns * 2 * MathF.PI;
                    if (unwrappedPan < panMin) unwrappedPan += 2 * MathF.PI;
                    if (unwrappedPan > panMax) unwrappedPan -= 2 * MathF.PI;
                }

                var directPanDiff = MathF.Abs(unwrappedPan - prevPan);
                var flippedTilt = MathF.PI - tiltValue;
                var flippedPan = unwrappedPan + MathF.PI;
                while (flippedPan < panMin) flippedPan += 2 * MathF.PI;
                while (flippedPan > panMax) flippedPan -= 2 * MathF.PI;

                var flipPanDiff = MathF.Abs(flippedPan - prevPan);
                var directValid = (unwrappedPan >= panMin && unwrappedPan <= panMax);
                var flipValid = (flippedPan >= panMin && flippedPan <= panMax);

                if (flipValid && (!directValid || flipPanDiff < directPanDiff))
                {
                    panValue = flippedPan;
                    tiltValue = flippedTilt;
                }
                else { panValue = unwrappedPan; }
            }
            else if (!float.IsNaN(_lastPanTilt.X))
            {
                var panSpan = panMax - panMin;
                var turns = MathF.Round((_lastPanTilt.X - panValue) / (2 * MathF.PI));
                panValue = panValue + turns * 2 * MathF.PI;
                if (panValue < panMin) panValue += 2 * MathF.PI;
                if (panValue > panMax) panValue -= 2 * MathF.PI;
            }
            _lastPanTilt = new Vector2(panValue, tiltValue);

            if (InvertPan.GetValue(context)) panValue = panMax + panMin - panValue;
            if (InvertTilt.GetValue(context)) tiltValue = tiltMax + tiltMin - tiltValue;

            panValue = Math.Clamp(panValue, panMin, panMax);
            tiltValue = Math.Clamp(tiltValue, tiltMin, tiltMax);

            SetDmxCoarseFine(panValue, PanChannel.GetValue(context), PanFineChannel.GetValue(context), panMin, panMax, panMax - panMin);
            SetDmxCoarseFine(tiltValue, TiltChannel.GetValue(context), TiltFineChannel.GetValue(context), tiltMin, tiltMax, tiltMax - tiltMin);
        }

        private void ProcessPosition(EvaluationContext context, Point point, Point referencePoint, bool calculateRelativePosition)
        {
            var measureAxis = (AxisModes)PositionMeasureAxis.GetValue(context);
            var invertDirection = InvertPositionDirection.GetValue(context);
            var distanceRange = PositionDistanceRange.GetValue(context);

            Vector3 actualPosition = point.Position;
            Vector3 effectiveReference = Vector3.Zero;

            if (calculateRelativePosition)
            {
                effectiveReference = referencePoint.Position;
            }

            float currentDistance = 0f;

            switch (measureAxis)
            {
                case AxisModes.X:
                    currentDistance = actualPosition.X - effectiveReference.X;
                    break;
                case AxisModes.Y:
                    currentDistance = actualPosition.Y - effectiveReference.Y;
                    break;
                case AxisModes.Z:
                    currentDistance = actualPosition.Z - effectiveReference.Z;
                    break;
            }

            if (invertDirection)
            {
                currentDistance = -currentDistance;
            }

            float inMin = distanceRange.X;
            float inMax = distanceRange.Y;

            if (Math.Abs(inMax - inMin) < 0.0001f)
            {
                Log.Warning("PositionDistanceRange Min and Max values are too close or identical. DMX output for position will be 0.", this);
                SetDmxCoarseFine(0, PositionChannel.GetValue(context), PositionFineChannel.GetValue(context), 0, 1, 1);
                return;
            }

            float rangeLength = inMax - inMin;

            SetDmxCoarseFine(currentDistance, PositionChannel.GetValue(context), PositionFineChannel.GetValue(context), inMin, inMax, rangeLength);
        }

        private void SetDmxCoarseFine(float value, int coarseChannel, int fineChannel, float inMin, float inMax, float maxRange)
        {
            var dmx16 = MapToDmx16(value, inMin, inMax, maxRange);
            if (fineChannel > 0)
            {
                InsertOrSet(coarseChannel - 1, (dmx16 >> 8) & 0xFF);
                InsertOrSet(fineChannel - 1, dmx16 & 0xFF);
            }
            else if (coarseChannel > 0)
            {
                InsertOrSet(coarseChannel - 1, (int)Math.Round((dmx16 / 65535.0f) * 255.0f));
            }
        }

        private int MapToDmx16(float value, float inMin, float inMax, float maxRange)
        {
            var range = inMax - inMin;
            if (Math.Abs(range) < 0.0001f || float.IsNaN(range)) return 0;

            var normalizedValue = (value - inMin) / range;
            return (int)Math.Round(Math.Clamp(normalizedValue, 0f, 1f) * 65535.0f);
        }

        private void ProcessF1(EvaluationContext context, Point point) { if (!float.IsNaN(point.F1)) InsertOrSet(F1Channel.GetValue(context) - 1, (int)Math.Round(point.F1 * 255.0f)); }
        private void ProcessF2(EvaluationContext context, Point point) { if (!float.IsNaN(point.F2)) InsertOrSet(F2Channel.GetValue(context) - 1, (int)Math.Round(point.F2 * 255.0f)); }

        private void InsertOrSet(int index, int value)
        {
            if (index < 0) return;
            if (index >= _pointChannelValues.Count)
            {
                // This can happen if FixtureChannelSize is smaller than the color block. We can either log or ignore.
                // For now, we'll log, as it's a configuration issue.
                Log.Warning($"DMX Channel index {index + 1} is out of range (list size: {_pointChannelValues.Count}). Adjust FixtureChannelSize or Channel Assignments.", this);
                return;
            }
            _pointChannelValues[index] = value;
        }

        private enum RotationModes { YXZ, YZX, ZYX, ZXY, XZY, XYZ, LegacyZ, ForReferencePoints }
        private enum AxisModes { X, Y, Z }

        private readonly List<int> _pointChannelValues = [];
        private readonly StructuredBufferReadAccess _pointsBufferReader = new();
        private readonly StructuredBufferReadAccess _referencePointsBufferReader = new();

        [Input(Guid = "61b48e46-c3d1-46e3-a470-810d55f30aa6")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> EffectedPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "2bea2ccb-89f2-427b-bd9a-95c7038b715e")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> ReferencePoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "1348ed7c-79f8-48c6-ac00-e60fb40050db")]
        public readonly InputSlot<int> FixtureChannelSize = new InputSlot<int>();

        [Input(Guid = "7449cd05-54be-484b-854a-d2143340f925")]
        public readonly InputSlot<bool> FitInUniverse = new InputSlot<bool>();

        [Input(Guid = "850af6c3-d9ef-492c-9cfb-e2589ae5b9ac")]
        public readonly InputSlot<bool> FillUniverse = new InputSlot<bool>();

        [Input(Guid = "df04fce0-c6e5-4039-b03f-e651fc0ec4a9")]
        public readonly InputSlot<bool> GetPosition = new InputSlot<bool>();

        [Input(Guid = "628d96a8-466b-4148-9658-7786833ec989", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> PositionMeasureAxis = new InputSlot<int>();

        [Input(Guid = "78a7e683-f4e7-4826-8e39-c8de08e50e5e")]
        public readonly InputSlot<bool> InvertPositionDirection = new InputSlot<bool>();

        [Input(Guid = "8880c101-403f-46e0-901e-20ec2dd333e9")]
        public readonly InputSlot<System.Numerics.Vector2> PositionDistanceRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "fc3ec0d6-8567-4d5f-9a63-5c69fb5988cb")]
        public readonly InputSlot<int> PositionChannel = new InputSlot<int>();

        [Input(Guid = "658a19df-e51b-45b4-9f91-cb97a891255a")]
        public readonly InputSlot<int> PositionFineChannel = new InputSlot<int>();

        [Input(Guid = "4922acd8-ab83-4394-8118-c555385c2ce9")]
        public readonly InputSlot<bool> GetRotation = new InputSlot<bool>();

        [Input(Guid = "ba8d8f32-792c-4675-a5f5-415c16db8c66", MappedType = typeof(RotationModes))]
        public readonly InputSlot<int> AxisOrder = new InputSlot<int>();

        [Input(Guid = "7bf3e057-b9eb-43d2-8e1a-64c1c3857ca1")]
        public readonly InputSlot<bool> InvertPan = new InputSlot<bool>();

        [Input(Guid = "f85ecf9f-0c3d-4c10-8ba7-480aa2c7a667")]
        public readonly InputSlot<bool> InvertTilt = new InputSlot<bool>();

        [Input(Guid = "e96655be-6bc7-4ca4-bf74-079a07570d74")]
        public readonly InputSlot<bool> ShortestPathPanTilt = new InputSlot<bool>();

        [Input(Guid = "f50da250-606d-4a15-a25e-5458f540e527")]
        public readonly InputSlot<System.Numerics.Vector2> PanRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "9000c279-73e4-4de8-a1f8-c3914eaaf533")]
        public readonly InputSlot<int> PanChannel = new InputSlot<int>();

        [Input(Guid = "4d4b3425-e6ad-4834-a8a7-06c9f9c2b909")]
        public readonly InputSlot<int> PanFineChannel = new InputSlot<int>();

        [Input(Guid = "6e8b4125-0e8c-430b-897d-2231bb4c8f6f")]
        public readonly InputSlot<System.Numerics.Vector2> TiltRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "47d7294f-6f73-4e21-ac9a-0fc0817283fb")]
        public readonly InputSlot<int> TiltChannel = new InputSlot<int>();

        [Input(Guid = "4a40e022-d206-447c-bda3-d534f231c816")]
        public readonly InputSlot<int> TiltFineChannel = new InputSlot<int>();

        [Input(Guid = "5cdc69f7-45ec-4eec-bfb6-960d6245dafb")]
        public readonly InputSlot<bool> GetColor = new InputSlot<bool>();

        [Input(Guid = "cf2c3308-8f3f-442d-a563-b419f12e7ad1")]
        public readonly InputSlot<bool> RGBToCMY = new InputSlot<bool>();

        [Input(Guid = "013cc355-91d6-4ea6-b9f7-f1817b89e4a3")]
        public readonly InputSlot<int> RedChannel = new InputSlot<int>();

        [Input(Guid = "970769f4-116f-418d-87a7-cda28e44d063")]
        public readonly InputSlot<int> GreenChannel = new InputSlot<int>();

        [Input(Guid = "d755342b-9a9e-4c78-8376-81579d8c0909")]
        public readonly InputSlot<int> BlueChannel = new InputSlot<int>();

        [Input(Guid = "f13edebd-b44f-49e9-985e-7e3feb886fea")]
        public readonly InputSlot<int> AlphaChannel = new InputSlot<int>();

        [Input(Guid = "8ceece78-9a08-4c7b-8fea-740e8e5929a6")]
        public readonly InputSlot<int> WhiteChannel = new InputSlot<int>();

        [Input(Guid = "91c78090-be10-4203-827e-d2ef1b93317e")]
        public readonly InputSlot<bool> GetF1 = new InputSlot<bool>();

        [Input(Guid = "b7061834-66aa-4f7f-91f9-10ebfe16713f")]
        public readonly InputSlot<int> F1Channel = new InputSlot<int>();

        [Input(Guid = "1cb93e97-0161-4a77-bbc7-ff30c1972cf8")]
        public readonly InputSlot<bool> GetF2 = new InputSlot<bool>();

        [Input(Guid = "d77be0d1-5fb9-4d26-9e4a-e16497e4759c")]
        public readonly InputSlot<int> F2Channel = new InputSlot<int>();

        [Input(Guid = "25e5f0ce-5ec8-4c99-beb1-317c6911a128")]
        public readonly InputSlot<bool> SetCustomVar1 = new InputSlot<bool>();

        [Input(Guid = "b08c920f-0d6b-4820-bc2d-81a47d5f1147")]
        public readonly InputSlot<int> CustomVar1Channel = new InputSlot<int>();

        [Input(Guid = "50e849e8-5582-432e-98f7-d8e036273864")]
        public readonly InputSlot<int> CustomVar1 = new InputSlot<int>();

        [Input(Guid = "18cc3a73-3a1a-4370-87b7-e5cd44f4a3ab")]
        public readonly InputSlot<bool> SetCustomVar2 = new InputSlot<bool>();

        [Input(Guid = "098f1662-6f47-4dd0-9a73-4c4814aefb23")]
        public readonly InputSlot<int> CustomVar2Channel = new InputSlot<int>();

        [Input(Guid = "e7a48fe0-d788-4f12-a9d4-52472519da09")]
        public readonly InputSlot<int> CustomVar2 = new InputSlot<int>();

        [Input(Guid = "876ef5b5-f2c6-4501-9e55-00b9a553a2e3")]
        public readonly InputSlot<bool> SetCustomVar3 = new InputSlot<bool>();

        [Input(Guid = "ac9a709e-6dc0-40ca-9f70-350e655a2630")]
        public readonly InputSlot<int> CustomVar3Channel = new InputSlot<int>();

        [Input(Guid = "d16d7c5c-2795-4fde-85fd-13b515191fbe")]
        public readonly InputSlot<int> CustomVar3 = new InputSlot<int>();

        [Input(Guid = "8dd3fc1c-cd94-4bf0-b948-d6f734916d49")]
        public readonly InputSlot<bool> SetCustomVar4 = new InputSlot<bool>();

        [Input(Guid = "cbaf821c-0305-4c74-a632-864081cc9a34")]
        public readonly InputSlot<int> CustomVar4Channel = new InputSlot<int>();

        [Input(Guid = "b29ebe11-89cb-4f86-aee0-cf729fa0d62c")]
        public readonly InputSlot<int> CustomVar4 = new InputSlot<int>();

        [Input(Guid = "a9315f88-6024-42e9-9691-4544627f0bef")]
        public readonly InputSlot<bool> SetCustomVar5 = new InputSlot<bool>();

        [Input(Guid = "7c59a5fb-052a-443c-9e10-cf859fe25658")]
        public readonly InputSlot<int> CustomVar5Channel = new InputSlot<int>();

        [Input(Guid = "58cc3eee-e81e-4bab-b12c-e7bc3cf62dd0")]
        public readonly InputSlot<int> CustomVar5 = new InputSlot<int>();
    }
}