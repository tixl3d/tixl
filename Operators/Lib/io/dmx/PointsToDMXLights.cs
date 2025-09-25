using SharpDX;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Utils;

namespace Lib.io.dmx
{
    [Guid("c9d7cd19-7fc6-4491-8dfa-3808725c7857")]
    public sealed class PointsToDMXLights : Instance<PointsToDMXLights>
    {
        [Output(Guid = "8DC2DB32-D7A3-4B3A-A000-93C3107D19E4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<List<int>> Result = new(new List<int>(20));

        [Output(Guid = "da7deb8c-4218-4cae-9ec5-fd7c2e6f4c35")]
        public readonly Slot<BufferWithViews> VisualizeLights = new();

        public PointsToDMXLights()
        {
            Result.UpdateAction = Update;
        }

        private readonly List<int> _resultItems = [128];
        private const int UniverseSize = 512;

        private BufferWithViews _visualizeBuffer;
        private Point[] _visualizationPoints = [];

        private void Update(EvaluationContext context)
        {
            var pointBuffer = EffectedPoints.GetValue(context);
            var referencePointBuffer = ReferencePoints.GetValue(context);

            if (pointBuffer == null)
            {
                Log.Warning("EffectedPoints buffer is not connected.", this);
                Result.Value.Clear();
                VisualizeLights.Value = null;
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
                if (_visualizationPoints.Length != _points.Length)
                {
                    _visualizationPoints = new Point[_points.Length];
                }

                UpdateChannelData(context, _points);
                Result.Value = _resultItems;

                UpdateVisualizationBuffer();
                VisualizeLights.Value = _visualizeBuffer;
            }
            else
            {
                Result.Value.Clear();
                VisualizeLights.Value = null;
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
            var debugToLog = DebugToLog.GetValue(context);

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

            // Reverted to a standard for loop to ensure ShortestPathPanTilt works correctly.
            for (var fixtureIndex = 0; fixtureIndex < fixtureCount; fixtureIndex++)
            {
                bool shouldLogThisFixture = debugToLog && fixtureIndex == 0;
                for (var i = 0; i < fixtureChannelSize; i++) { _pointChannelValues[i] = 0; }

                var firstPointIndexForFixture = fixtureIndex * pixelsPerFixture;
                var transformPoint = points[firstPointIndexForFixture];
                Point referencePoint = useReferencePoints ? _referencePoints[fixtureIndex] : transformPoint;

                if (shouldLogThisFixture) Log.Debug("--- Fixture 0 Debug ---", this);

                // Process Position and Rotation
                var finalVisOrientation = ProcessTransformations(context, transformPoint, referencePoint, useReferencePoints, shouldLogThisFixture,
                                                                 out var finalVisPosition);

                // Update visualization points
                for (var pixelIndex = 0; pixelIndex < pixelsPerFixture; ++pixelIndex)
                {
                    var currentIndex = firstPointIndexForFixture + pixelIndex;
                    if (currentIndex < _visualizationPoints.Length)
                    {
                        _visualizationPoints[currentIndex] = points[currentIndex];
                        _visualizationPoints[currentIndex].Position = finalVisPosition;
                        _visualizationPoints[currentIndex].Orientation = finalVisOrientation;
                    }
                }

                // Handle Color, Features, and Custom Variables
                HandleColorAndFeatures(context, points, transformPoint, firstPointIndexForFixture, pixelsPerFixture);
                HandleCustomVariables(context);

                // Add fixture channels to the main result list
                if (fitInUniverse)
                {
                    var remainingInUniverse = UniverseSize - (_resultItems.Count % UniverseSize);
                    if (fixtureChannelSize > remainingInUniverse)
                    {
                        for (var i = 0; i < remainingInUniverse; i++) { _resultItems.Add(0); }
                    }
                }
                _resultItems.AddRange(_pointChannelValues);
            }

            if (fillUniverse)
            {
                var remainder = _resultItems.Count % UniverseSize;
                if (remainder != 0)
                {
                    var toAdd = UniverseSize - remainder;
                    for (var i = 0; i < toAdd; i++) { _resultItems.Add(0); }
                }
            }
        }

        private Quaternion ProcessTransformations(EvaluationContext context, Point transformPoint, Point referencePoint, bool useReferencePoints, bool shouldLog, out Vector3 finalVisPosition)
        {
            var getRotation = GetRotation.GetValue(context);
            var getPosition = GetPosition.GetValue(context);

            var finalVisOrientation = transformPoint.Orientation;
            if (getRotation)
            {
                finalVisOrientation = ProcessRotation(context, transformPoint, referencePoint, useReferencePoints, shouldLog);
            }

            finalVisPosition = transformPoint.Position;
            if (getPosition)
            {
                finalVisPosition = ProcessPosition(context, transformPoint, referencePoint, useReferencePoints, shouldLog);
            }
            else if (getRotation)
            {
                finalVisPosition = referencePoint.Position;
            }

            return finalVisOrientation;
        }

        private void HandleColorAndFeatures(EvaluationContext context, Point[] points, Point transformPoint, int firstPointIndex, int pixelsPerFixture)
        {
            var getColor = GetColor.GetValue(context);
            var getF1 = GetF1.GetValue(context);
            var getF2 = GetF2.GetValue(context);
            var getF1ByPixel = GetF1ByPixel.GetValue(context);
            var getF2ByPixel = GetF2ByPixel.GetValue(context);
            var f1Ch = F1Channel.GetValue(context);
            var f2Ch = F2Channel.GetValue(context);
            var redCh = RedChannel.GetValue(context);

            bool hasPerPixel = getColor || (getF1 && getF1ByPixel) || (getF2 && getF2ByPixel);

            if (hasPerPixel)
            {
                int startCh = redCh;
                if (!getColor || redCh <= 0)
                {
                    if (getF1 && getF1ByPixel && f1Ch > 0) startCh = f1Ch;
                    else if (getF2 && getF2ByPixel && f2Ch > 0) startCh = f2Ch;
                }
                var currentDmxChannelIndex = startCh - 1;
                var useCmy = RGBToCMY.GetValue(context);
                var greenCh = GreenChannel.GetValue(context);
                var blueCh = BlueChannel.GetValue(context);
                var whiteCh = WhiteChannel.GetValue(context);
                var alphaCh = AlphaChannel.GetValue(context);

                for (var pixelIndex = 0; pixelIndex < pixelsPerFixture; pixelIndex++)
                {
                    var pointForPerPixel = points[firstPointIndex + pixelIndex];

                    if (getColor && redCh > 0)
                    {
                        float r = float.IsNaN(pointForPerPixel.Color.X) ? 0f : Math.Clamp(pointForPerPixel.Color.X, 0f, 1f);
                        float g = float.IsNaN(pointForPerPixel.Color.Y) ? 0f : Math.Clamp(pointForPerPixel.Color.Y, 0f, 1f);
                        float b = float.IsNaN(pointForPerPixel.Color.Z) ? 0f : Math.Clamp(pointForPerPixel.Color.Z, 0f, 1f);

                        if (useCmy) { r = 1f - r; g = 1f - g; b = 1f - b; }

                        var vR = r * 255.0f;
                        var vG = g * 255.0f;
                        var vB = b * 255.0f;
                        var vW = Math.Min(vR, Math.Min(vG, vB));
                        var vA = pointForPerPixel.Color.W * 255f;

                        if (redCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vR));
                        if (greenCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vG));
                        if (blueCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vB));
                        if (whiteCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vW));
                        if (alphaCh > 0) InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(vA));
                    }

                    if (getF1 && getF1ByPixel && f1Ch > 0)
                    {
                        float f1 = float.IsNaN(pointForPerPixel.F1) ? 0f : Math.Clamp(pointForPerPixel.F1, 0f, 1f);
                        InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(f1 * 255.0f));
                    }

                    if (getF2 && getF2ByPixel && f2Ch > 0)
                    {
                        float f2 = float.IsNaN(pointForPerPixel.F2) ? 0f : Math.Clamp(pointForPerPixel.F2, 0f, 1f);
                        InsertOrSet(currentDmxChannelIndex++, (int)Math.Round(f2 * 255.0f));
                    }
                }
            }

            if (getF1 && !getF1ByPixel && f1Ch > 0)
            {
                float f1 = float.IsNaN(transformPoint.F1) ? 0f : Math.Clamp(transformPoint.F1, 0f, 1f);
                InsertOrSet(f1Ch - 1, (int)Math.Round(f1 * 255.0f));
            }

            if (getF2 && !getF2ByPixel && f2Ch > 0)
            {
                float f2 = float.IsNaN(transformPoint.F2) ? 0f : Math.Clamp(transformPoint.F2, 0f, 1f);
                InsertOrSet(f2Ch - 1, (int)Math.Round(f2 * 255.0f));
            }
        }

        private void HandleCustomVariables(EvaluationContext context)
        {
            if (SetCustomVar1.GetValue(context) && CustomVar1Channel.GetValue(context) > 0) InsertOrSet(CustomVar1Channel.GetValue(context) - 1, CustomVar1.GetValue(context));
            if (SetCustomVar2.GetValue(context) && CustomVar2Channel.GetValue(context) > 0) InsertOrSet(CustomVar2Channel.GetValue(context) - 1, CustomVar2.GetValue(context));
            if (SetCustomVar3.GetValue(context) && CustomVar3Channel.GetValue(context) > 0) InsertOrSet(CustomVar3Channel.GetValue(context) - 1, CustomVar3.GetValue(context));
            if (SetCustomVar4.GetValue(context) && CustomVar4Channel.GetValue(context) > 0) InsertOrSet(CustomVar4Channel.GetValue(context) - 1, CustomVar4.GetValue(context));
            if (SetCustomVar5.GetValue(context) && CustomVar5Channel.GetValue(context) > 0) InsertOrSet(CustomVar5Channel.GetValue(context) - 1, CustomVar5.GetValue(context));
        }

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

            ResourceManager.SetupStructuredBuffer(_visualizationPoints,
                                                  stride * pointCount,
                                                  stride,
                                                  ref buffer);

            ResourceManager.CreateStructuredBufferSrv(buffer, ref srv);
            ResourceManager.CreateStructuredBufferUav(buffer, UnorderedAccessViewBufferFlags.None, ref uav);

            if (_visualizeBuffer == null)
                _visualizeBuffer = new BufferWithViews();

            _visualizeBuffer.Buffer = buffer;
            _visualizeBuffer.Srv = srv;
            _visualizeBuffer.Uav = uav;
        }

        private Vector2 _lastPanTilt = new(float.NaN, float.NaN);
        private Point[] _points = [];
        private Point[] _referencePoints = [];

        // --- THIS METHOD IS REVERTED TO THE ORIGINAL WORKING LOGIC ---
        private Quaternion ProcessRotation(EvaluationContext context, Point point, Point referencePoint, bool calculateRelativeRotation, bool shouldLog)
        {
            var panAxisSelection = (AxisModes)PanAxis.GetValue(context);
            var tiltAxisSelection = (AxisModes)TiltAxis.GetValue(context);
            var panChannel = PanChannel.GetValue(context);
            var tiltChannel = TiltChannel.GetValue(context);

            bool isPanOutputEnabled = panAxisSelection != AxisModes.Disabled && panChannel > 0;
            bool isTiltOutputEnabled = tiltAxisSelection != AxisModes.Disabled && tiltChannel > 0;

            if (!isPanOutputEnabled && !isTiltOutputEnabled)
            {
                return point.Orientation;
            }

            var rotation = point.Orientation;
            if (float.IsNaN(rotation.X) || float.IsNaN(rotation.Y) || float.IsNaN(rotation.Z) || float.IsNaN(rotation.W))
                return point.Orientation;

            var forwardVectorSelection = (ForwardVectorModes)ForwardVector.GetValue(context);
            Vector3 initialForwardAxis;
            switch (forwardVectorSelection)
            {
                case ForwardVectorModes.X: initialForwardAxis = Vector3.UnitX; break;
                case ForwardVectorModes.Y: initialForwardAxis = Vector3.UnitY; break;
                case ForwardVectorModes.Z: initialForwardAxis = Vector3.UnitZ; break;
                case ForwardVectorModes.NegX: initialForwardAxis = -Vector3.UnitX; break;
                case ForwardVectorModes.NegY: initialForwardAxis = -Vector3.UnitY; break;
                case ForwardVectorModes.NegZ: initialForwardAxis = -Vector3.UnitZ; break;
                default: initialForwardAxis = Vector3.UnitZ; break;
            }

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

            if (InvertX.GetValue(context)) direction.X = -direction.X;
            if (InvertY.GetValue(context)) direction.Y = -direction.Y;
            if (InvertZ.GetValue(context)) direction.Z = -direction.Z;

            direction = Vector3.Normalize(direction);
            if (shouldLog) Log.Debug($"Direction: {direction:F3}", this);

            float rawPanValue = 0, rawTiltValue = 0;

            Vector3 GetAxisVector(AxisModes axis)
            {
                switch (axis)
                {
                    case AxisModes.X: return Vector3.UnitX;
                    case AxisModes.Y: return Vector3.UnitY;
                    case AxisModes.Z: return Vector3.UnitZ;
                    case AxisModes.Disabled: return Vector3.Zero;
                    default: return Vector3.UnitY;
                }
            }

            var panAxisVec = GetAxisVector(panAxisSelection);
            var tiltAxisVec = GetAxisVector(tiltAxisSelection);

            if (panAxisVec == Vector3.Zero && tiltAxisVec == Vector3.Zero)
            {
            }
            else if (panAxisVec == Vector3.Zero)
            {
                rawPanValue = 0;
                var projectedDirection = direction - Vector3.Dot(direction, tiltAxisVec) * tiltAxisVec;
                if (projectedDirection.LengthSquared() > 1e-6f)
                {
                    rawTiltValue = MathF.Asin(Vector3.Dot(direction, tiltAxisVec));
                }
                else
                {
                    rawTiltValue = 0;
                }
            }
            else if (tiltAxisVec == Vector3.Zero)
            {
                rawTiltValue = 0;
                var projectedDirection = direction - Vector3.Dot(direction, panAxisVec) * panAxisVec;
                if (projectedDirection.LengthSquared() > 1e-6f)
                {
                    rawPanValue = MathF.Atan2(Vector3.Dot(direction, Vector3.Cross(panAxisVec, initialForwardAxis)),
                                              Vector3.Dot(direction, initialForwardAxis));
                }
                else
                {
                    rawPanValue = 0;
                }
            }
            else
            {
                if (panAxisSelection == tiltAxisSelection)
                {
                    Log.Warning("Pan and Tilt axes cannot be the same when both are enabled. Halting rotation calculation for this fixture.", this);
                    rawPanValue = 0;
                    rawTiltValue = 0;
                }
                else
                {
                    var fwdAxisVec = Vector3.Cross(tiltAxisVec, panAxisVec);
                    if (fwdAxisVec.LengthSquared() < 1e-6f)
                    {
                        Log.Error("Pan and Tilt axes are collinear, cannot form a unique forward vector. Check axis selections.", this);
                        rawPanValue = 0;
                        rawTiltValue = 0;
                    }
                    else
                    {
                        fwdAxisVec = Vector3.Normalize(fwdAxisVec);
                        var upComponent = Vector3.Dot(direction, panAxisVec);
                        var rightComponent = Vector3.Dot(direction, tiltAxisVec);
                        var forwardComponent = Vector3.Dot(direction, fwdAxisVec);
                        rawPanValue = MathF.Atan2(rightComponent, forwardComponent);
                        rawTiltValue = MathF.Asin(Math.Clamp(upComponent, -1f, 1f));
                    }
                }
            }

            if (shouldLog) Log.Debug($"Raw Angles (rad): Pan={rawPanValue:F3}, Tilt={rawTiltValue:F3}", this);

            float finalPanValue = 0f;
            if (isPanOutputEnabled)
            {
                var panRange = PanRange.GetValue(context);
                if (panRange.X >= panRange.Y)
                {
                    Log.Warning("Pan: Min range value must be less than max.", this);
                }
                else
                {
                    var panMin = panRange.X * MathF.PI / 180f;
                    var panMax = panRange.Y * MathF.PI / 180f;
                    var panValue = rawPanValue;

                    if (ShortestPathPanTilt.GetValue(context) && !float.IsNaN(_lastPanTilt.X))
                    {
                        var prevPan = _lastPanTilt.X;
                        var panSpan = panMax - panMin;
                        if (panSpan > MathF.PI * 1.5f)
                        {
                            var turns = MathF.Round((prevPan - panValue) / (2 * MathF.PI));
                            panValue = panValue + turns * 2 * MathF.PI;
                            if (panValue < panMin - MathF.PI) panValue += 2 * MathF.PI;
                            if (panValue > panMax + MathF.PI) panValue -= 2 * MathF.PI;
                        }
                    }

                    if (InvertPan.GetValue(context)) panValue = panMax + panMin - panValue;

                    finalPanValue = Math.Clamp(panValue, panMin, panMax);
                    if (shouldLog) Log.Debug($"Final Pan Angle (rad): {finalPanValue:F3}", this);

                    SetDmxCoarseFine(finalPanValue, panChannel, PanFineChannel.GetValue(context), panMin, panMax, panMax - panMin, shouldLog, "Pan");
                }
            }

            float finalTiltValue = 0f;
            if (isTiltOutputEnabled)
            {
                var tiltRange = TiltRange.GetValue(context);
                if (tiltRange.X >= tiltRange.Y)
                {
                    Log.Warning("Tilt: Min range value must be less than max.", this);
                }
                else
                {
                    var tiltMin = tiltRange.X * MathF.PI / 180f;
                    var tiltMax = tiltRange.Y * MathF.PI / 180f;
                    var tiltValue = rawTiltValue;

                    if (InvertTilt.GetValue(context)) tiltValue = tiltMax + tiltMin - tiltValue;

                    finalTiltValue = Math.Clamp(tiltValue, tiltMin, tiltMax);
                    if (shouldLog) Log.Debug($"Final Tilt Angle (rad): {finalTiltValue:F3}", this);

                    SetDmxCoarseFine(finalTiltValue, tiltChannel, TiltFineChannel.GetValue(context), tiltMin, tiltMax, tiltMax - tiltMin, shouldLog, "Tilt");
                }
            }

            _lastPanTilt = new Vector2(finalPanValue, finalTiltValue);

            var panRotation = isPanOutputEnabled ? Quaternion.CreateFromAxisAngle(panAxisVec, finalPanValue) : Quaternion.Identity;
            var tiltRotation = isTiltOutputEnabled ? Quaternion.CreateFromAxisAngle(tiltAxisVec, finalTiltValue) : Quaternion.Identity;

            Quaternion calculatedOutputRotation;
            var rotationOrder = (RotationOrderModes)RotationOrder.GetValue(context);

            if (rotationOrder == RotationOrderModes.TiltThenPan)
            {
                calculatedOutputRotation = tiltRotation * panRotation;
            }
            else
            {
                calculatedOutputRotation = panRotation * tiltRotation;
            }

            if (calculateRelativeRotation)
            {
                calculatedOutputRotation = referencePoint.Orientation * calculatedOutputRotation;
            }

            return calculatedOutputRotation;
        }

        private Vector3 ProcessPosition(EvaluationContext context, Point point, Point referencePoint, bool calculateRelativePosition, bool shouldLog)
        {
            var positionChannel = PositionChannel.GetValue(context);
            var positionAxis = (AxisModes)PositionMeasureAxis.GetValue(context);

            if (positionChannel <= 0 || positionAxis == AxisModes.Disabled)
            {
                return point.Position;
            }

            var invertDirection = InvertPositionDirection.GetValue(context);
            var distanceRange = PositionDistanceRange.GetValue(context);

            Vector3 actualPosition = point.Position;
            Vector3 effectiveReference = Vector3.Zero;
            Vector3 finalPosition = actualPosition;

            if (calculateRelativePosition)
            {
                effectiveReference = referencePoint.Position;
            }

            float currentDistance = 0f;

            switch (positionAxis)
            {
                case AxisModes.X: currentDistance = actualPosition.X - effectiveReference.X; break;
                case AxisModes.Y: currentDistance = actualPosition.Y - effectiveReference.Y; break;
                case AxisModes.Z: currentDistance = actualPosition.Z - effectiveReference.Z; break;
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
                SetDmxCoarseFine(0, positionChannel, PositionFineChannel.GetValue(context), 0, 1, 1, shouldLog, "Position");
                return point.Position;
            }

            float clampedDistance = Math.Clamp(currentDistance, inMin, inMax);
            float rangeLength = inMax - inMin;

            SetDmxCoarseFine(clampedDistance, positionChannel, PositionFineChannel.GetValue(context), inMin, inMax, rangeLength, shouldLog, "Position");

            float finalClampedDistance = invertDirection ? -clampedDistance : clampedDistance;
            switch (positionAxis)
            {
                case AxisModes.X: finalPosition.X = effectiveReference.X + finalClampedDistance; break;
                case AxisModes.Y: finalPosition.Y = effectiveReference.Y + finalClampedDistance; break;
                case AxisModes.Z: finalPosition.Z = effectiveReference.Z + finalClampedDistance; break;
            }

            return finalPosition;
        }

        private void SetDmxCoarseFine(float value, int coarseChannel, int fineChannel, float inMin, float inMax, float maxRange, bool shouldLog, string name)
        {
            var dmx16 = MapToDmx16(value, inMin, inMax, maxRange);
            if (shouldLog) Log.Debug($"{name} DMX (16bit): {dmx16}", this);

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

        private void InsertOrSet(int index, int value)
        {
            if (index < 0) return;
            if (index >= _pointChannelValues.Count)
            {
                Log.Warning($"DMX Channel index {index + 1} is out of range (list size: {_pointChannelValues.Count}). Adjust FixtureChannelSize or Channel Assignments.", this);
                return;
            }
            _pointChannelValues[index] = value;
        }

        private enum AxisModes { Disabled, X, Y, Z }
        private enum RotationOrderModes { PanThenTilt, TiltThenPan }
        private enum ForwardVectorModes { X, Y, Z, NegX, NegY, NegZ }

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

        [Input(Guid = "23F23213-68E2-45F5-B452-4A86289004C0")]
        public readonly InputSlot<bool> DebugToLog = new InputSlot<bool>();

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

        [Input(Guid = "032F3617-E1F3-4B41-A3BE-61DD63B9F3BA", MappedType = typeof(ForwardVectorModes))]
        public readonly InputSlot<int> ForwardVector = new InputSlot<int>();

        [Input(Guid = "9c235473-346b-4861-9844-4b584e09f58a", MappedType = typeof(RotationOrderModes))]
        public readonly InputSlot<int> RotationOrder = new InputSlot<int>();

        [Input(Guid = "49fefbdb-2652-43db-ae52-ebc2df3e2856")]
        public readonly InputSlot<bool> InvertX = new InputSlot<bool>();

        [Input(Guid = "6d8fc457-0c80-4736-8c25-cc48f07cbbfd")]
        public readonly InputSlot<bool> InvertY = new InputSlot<bool>();

        [Input(Guid = "0c57cdd5-e450-4425-954f-c9e4256f83e1")]
        public readonly InputSlot<bool> InvertZ = new InputSlot<bool>();

        [Input(Guid = "1f532994-fb0e-44e4-8a80-7917e1851eae", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> PanAxis = new InputSlot<int>();

        [Input(Guid = "7bf3e057-b9eb-43d2-8e1a-64c1c3857ca1")]
        public readonly InputSlot<bool> InvertPan = new InputSlot<bool>();

        [Input(Guid = "1f877cf6-10d9-4d0b-b087-974bd6855e0a", MappedType = typeof(AxisModes))]
        public readonly InputSlot<int> TiltAxis = new InputSlot<int>();

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

        [Input(Guid = "bec9e5a6-40a9-49b2-88bd-01a4ea03d28c")]
        public readonly InputSlot<bool> GetF1ByPixel = new InputSlot<bool>();

        [Input(Guid = "b7061834-66aa-4f7f-91f9-10ebfe16713f")]
        public readonly InputSlot<int> F1Channel = new InputSlot<int>();

        [Input(Guid = "1cb93e97-0161-4a77-bbc7-ff30c1972cf8")]
        public readonly InputSlot<bool> GetF2 = new InputSlot<bool>();

        [Input(Guid = "b8080f4e-4542-4e20-9844-8028bbaf223f")]
        public readonly InputSlot<bool> GetF2ByPixel = new InputSlot<bool>();

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