using System;
using System.Collections.Generic;
using System.Numerics;

namespace Lib.physics;

/// <summary>
/// Simulates the rigid bodies and joints declared by the surrounding graph and publishes their poses as a
/// point buffer. The simulation itself lives in <see cref="PhysicsSimulation"/>; this operator only gathers
/// inputs and owns the GPU buffer. Body and joint operators stay pure data producers, which keeps them free
/// of Bepu types and makes the world the only mutable state.
/// </summary>
[Guid("88af1ae4-2758-4349-8d5a-ac0a9e5af97a")]
internal sealed class PhysicsWorld : Instance<PhysicsWorld>
{
    [Output(Guid = "f76bac73-a7e4-4ccb-8cf4-e3cc6e5bd0b8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Output(Guid = "43f606b9-4879-4435-b453-00c1a2d10ff3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> BodyCount = new();

    [Output(Guid = "e10cf7f7-d78d-4a5f-9afb-36e18b44e513", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> SimTime = new();

    /// <summary>The impacts of this frame as points. Always <see cref="MaxContacts"/> long; unused points have zero scale.</summary>
    [Output(Guid = "ae24bbd8-298c-47b1-99ad-30b2878623ac", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> Contacts = new();

    [Output(Guid = "a38d9319-fcf9-4399-aaeb-7a0b430e6ddb", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> ContactCount = new();

    public PhysicsWorld()
    {
        _simulation = new PhysicsSimulation(this);
        OutBuffer.UpdateAction += Update;
        BodyCount.UpdateAction += Update;
        SimTime.UpdateAction += Update;
        Contacts.UpdateAction += Update;
        ContactCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            UpdateSimulation(context);
            _hasLoggedError = false;
        }
        catch (Exception e)
        {
            // A physics exception must never escape into the evaluator; the editor treats it as fatal.
            if (!_hasLoggedError)
            {
                Log.Error($"PhysicsWorld update failed: {e.Message}", this);
                _hasLoggedError = true;
            }

            // Release disposes Bepu state and can itself fail while the simulation is already broken;
            // a throw here would escape the catch and defeat the handler above.
            try
            {
                _simulation.Release();
            }
            catch (Exception releaseException)
            {
                Log.Error($"PhysicsWorld release after failure failed: {releaseException.Message}", this);
            }

            OutBuffer.Value = null;
            OutBuffer.DirtyFlag.Invalidate();
            BodyCount.Value = 0;
            BodyCount.DirtyFlag.Invalidate();
            SimTime.Value = 0;
            SimTime.DirtyFlag.Invalidate();
            Contacts.Value = null;
            Contacts.DirtyFlag.Invalidate();
            ContactCount.Value = 0;
            ContactCount.DirtyFlag.Invalidate();
        }
    }

    private void UpdateSimulation(EvaluationContext context)
    {
        CollectInputs(context);

        var deterministic = Deterministic.GetValue(context);
        var settings = new PhysicsWorldSettings
                           {
                               Gravity = Gravity.GetValue(context),
                               ContactStiffness = ContactStiffness.GetValue(context),
                               FixedRate = FixedRate.GetValue(context),
                               Substeps = Substeps.GetValue(context),
                               SolverIterations = SolverIterations.GetValue(context),
                               TimeScale = TimeScale.GetValue(context),
                               Multithreaded = Multithreaded.GetValue(context),
                               Deterministic = deterministic,
                               Interpolate = Interpolate.GetValue(context),
                               Paused = Paused.GetValue(context),
                               Reset = Reset.GetValue(context),
                               ContactSpeedThreshold = ContactSpeed.GetValue(context),
                               BuildMeshesInBackground = !context.Playback.IsRenderingToFile && !deterministic,
                           };
        var includeStatics = IncludeStatics.GetValue(context);

        var now = context.Playback.SecondsFromBars(context.Playback.FxTimeInBars);
        _simulation.Update(settings, now, _frameBodies, _frameJoints, _frameForces);
        WritePoints(includeStatics);
        PublishBodyRanges();
        WriteContacts(MaxContacts.GetValue(context));

        BodyCount.Value = _simulation.RenderCount(includeStatics);
        BodyCount.DirtyFlag.Invalidate();
        SimTime.Value = _simulation.SimTime;
        SimTime.DirtyFlag.Invalidate();
    }

    private void CollectInputs(EvaluationContext context)
    {
        _frameBodies.Clear();
        var bodyInputs = Bodies.GetCollectedTypedInputs();
        if (bodyInputs != null)
        {
            for (var i = 0; i < bodyInputs.Count; i++)
            {
                var body = bodyInputs[i].GetValue(context);
                if (body != null)
                    _frameBodies.Add(body);
            }
        }

        _frameJoints.Clear();
        var jointInputs = Joints.GetCollectedTypedInputs();
        if (jointInputs != null)
        {
            for (var i = 0; i < jointInputs.Count; i++)
            {
                var joint = jointInputs[i].GetValue(context);
                if (joint != null)
                    _frameJoints.Add(joint);
            }
        }

        _frameForces.Clear();
        var forceInputs = Forces.GetCollectedTypedInputs();
        if (forceInputs != null)
        {
            for (var i = 0; i < forceInputs.Count; i++)
            {
                var force = forceInputs[i].GetValue(context);
                if (force != null)
                    _frameForces.Add(force);
            }
        }
    }

    /// <summary>
    /// Hands every body description its slice of this frame's poses, statics included, so [RigidBodyPoints] can
    /// draw one description's bodies with a mesh of their own.
    /// </summary>
    private void PublishBodyRanges()
    {
        var count = _simulation.RenderCount(true);
        if (_allPoints == null || _allPoints.Length != count)
            _allPoints = new Point[count];

        _simulation.WritePoints(_allPoints, true);
        _simulation.PublishBodyRanges(_allPoints, OutBuffer.Value);
    }

    /// <summary>
    /// The contact buffer keeps a fixed length, because consumers read the point count from the buffer size and a
    /// buffer that changed size with every impact would be reallocated most frames.
    /// </summary>
    private void WriteContacts(int maxContacts)
    {
        maxContacts = Math.Clamp(maxContacts, 0, 4096);
        if (maxContacts == 0 || ResourceManager.Device == null || !EnsureContactBuffer(maxContacts))
        {
            Contacts.Value = null;
            Contacts.DirtyFlag.Invalidate();
            ContactCount.Value = 0;
            ContactCount.DirtyFlag.Invalidate();
            return;
        }

        ContactCount.Value = _simulation.WriteContacts(_contactPoints);
        ContactCount.DirtyFlag.Invalidate();
        ResourceManager.Device.ImmediateContext.UpdateSubresource(_contactPoints, _contactBuffer);
        Contacts.Value = _contactBufferWithViews;
        Contacts.DirtyFlag.Invalidate();
    }

    private bool EnsureContactBuffer(int count)
    {
        var totalBytes = count * Point.Stride;
        if (_contactBuffer != null && _contactBuffer.Description.SizeInBytes == totalBytes)
            return true;

        ReleaseContactBuffer();
        ResourceManager.SetupStructuredBuffer(totalBytes, Point.Stride, ref _contactBuffer);
        if (_contactBuffer == null)
            return WarnBufferFailure($"PhysicsWorld: structured buffer creation failed for {totalBytes} bytes of contacts");

        ResourceManager.CreateStructuredBufferSrv(_contactBuffer, ref _contactBufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_contactBuffer, UnorderedAccessViewBufferFlags.None, ref _contactBufferWithViews.Uav);
        if (_contactBufferWithViews.Srv == null || _contactBufferWithViews.Uav == null)
        {
            ReleaseContactBuffer();
            return WarnBufferFailure("PhysicsWorld: contact buffer views failed");
        }

        _contactBufferWithViews.Buffer = _contactBuffer;
        _contactPoints = new Point[count];
        return true;
    }

    private void ReleaseContactBuffer()
    {
        _contactBufferWithViews.Srv?.Dispose();
        _contactBufferWithViews.Uav?.Dispose();
        _contactBufferWithViews.Srv = null;
        _contactBufferWithViews.Uav = null;
        _contactBufferWithViews.Buffer = null;
        _contactBuffer?.Dispose();
        _contactBuffer = null;
        _contactPoints = null;
    }

    private void WritePoints(bool includeStatics)
    {
        var count = _simulation.RenderCount(includeStatics);
        if (count > 0 && !EnsurePointBuffer(count))
            count = 0;

        if (count == 0)
        {
            OutBuffer.Value = null;
            OutBuffer.DirtyFlag.Invalidate();
            return;
        }

        _simulation.WritePoints(_points, includeStatics);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(_points, _pointBuffer);
        OutBuffer.Value = _bufferWithViews;
        OutBuffer.DirtyFlag.Invalidate();
    }

    private bool EnsurePointBuffer(int count)
    {
        if (ResourceManager.Device == null)
            return WarnBufferFailure("PhysicsWorld: no graphics device to create the point buffer with");

        var totalBytes = count * Point.Stride;
        if (_pointBuffer != null && _pointBuffer.Description.SizeInBytes == totalBytes)
            return true;

        ReleasePointBuffer();

        ResourceManager.SetupStructuredBuffer(totalBytes, Point.Stride, ref _pointBuffer);
        if (_pointBuffer == null)
            return WarnBufferFailure($"PhysicsWorld: structured buffer creation failed for {totalBytes} bytes");

        ResourceManager.CreateStructuredBufferSrv(_pointBuffer, ref _bufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_pointBuffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);
        if (_bufferWithViews.Srv == null || _bufferWithViews.Uav == null)
        {
            var detail = $"srv={_bufferWithViews.Srv != null}, uav={_bufferWithViews.Uav != null}";
            ReleasePointBuffer();
            return WarnBufferFailure($"PhysicsWorld: buffer views failed ({detail})");
        }

        _bufferWithViews.Buffer = _pointBuffer;
        _points = new Point[count];
        _hasLoggedBufferWarning = false;
        return true;
    }

    /// <summary>
    /// A missing device or a refusing driver leaves the output null, which downstream looks like
    /// "no bodies" - say why, but only once, or a broken frame would repeat it every evaluation.
    /// </summary>
    private bool WarnBufferFailure(string message)
    {
        if (!_hasLoggedBufferWarning)
        {
            Log.Warning(message, this);
            _hasLoggedBufferWarning = true;
        }

        return false;
    }

    private void ReleasePointBuffer()
    {
        _bufferWithViews.Srv?.Dispose();
        _bufferWithViews.Uav?.Dispose();
        _bufferWithViews.Srv = null;
        _bufferWithViews.Uav = null;
        _bufferWithViews.Buffer = null;
        _pointBuffer?.Dispose();
        _pointBuffer = null;
        _points = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _simulation.Dispose();
            ReleasePointBuffer();
            ReleaseContactBuffer();
        }

        base.Dispose(disposing);
    }

    private readonly PhysicsSimulation _simulation;
    private readonly List<PhysicsBody> _frameBodies = new();
    private readonly List<PhysicsJoint> _frameJoints = new();
    private readonly List<PhysicsForce> _frameForces = new();
    private Point[] _allPoints;
    private Point[] _contactPoints;
    private BufferWithViews _contactBufferWithViews = new();
    private Buffer _contactBuffer;
    private bool _hasLoggedError;
    private bool _hasLoggedBufferWarning;

    private Point[] _points;
    private BufferWithViews _bufferWithViews = new();
    private Buffer _pointBuffer;

    [Input(Guid = "138645fa-4742-4d28-961e-a5ffdc2b962a")]
    public readonly MultiInputSlot<PhysicsBody> Bodies = new();

    [Input(Guid = "d674cf34-39bf-4fab-884a-879eb4a27e26")]
    public readonly MultiInputSlot<PhysicsJoint> Joints = new();

    [Input(Guid = "508e2ee0-fe78-48e1-9e78-7b2a4c937cc4")]
    public readonly MultiInputSlot<PhysicsForce> Forces = new();

    [Input(Guid = "098ac911-44f7-45e4-99e4-80b18b7058ba")]
    public readonly InputSlot<Vector3> Gravity = new(new Vector3(0f, -9.81f, 0f));

    [Input(Guid = "01e66019-3f45-4933-895d-8cb2c5f8a757")]
    public readonly InputSlot<float> FixedRate = new(60f);

    [Input(Guid = "54247fde-224c-4764-97e1-01d1e725acdc")]
    public readonly InputSlot<int> Substeps = new(2);

    [Input(Guid = "93aea25c-08d7-49ba-8989-16881f1a475b")]
    public readonly InputSlot<int> SolverIterations = new(4);

    [Input(Guid = "4e9535a7-d7ed-4917-aa4e-df566c6b7328")]
    public readonly InputSlot<float> TimeScale = new(1f);

    [Input(Guid = "ffe4f804-dbc2-48c8-aa5e-3a4d0fb38407")]
    public readonly InputSlot<bool> Interpolate = new(true);

    [Input(Guid = "c2e25c12-d0fe-4593-a149-a324ff73b19b")]
    public readonly InputSlot<bool> IncludeStatics = new(false);

    [Input(Guid = "65c7e041-a308-407c-8bfe-a7f0f516279a")]
    public readonly InputSlot<bool> Multithreaded = new(true);

    [Input(Guid = "be5ef8d6-240b-4d61-9883-49e5f107d5d6")]
    public readonly InputSlot<bool> Deterministic = new(false);

    [Input(Guid = "1dd023b9-76b2-4455-86c2-fced2726528e")]
    public readonly InputSlot<float> ContactStiffness = new(30f);

    [Input(Guid = "8279a472-cae2-4427-8de7-2fe51bde82b4")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "a8d603b9-64ce-40ea-8593-ba4b4d67af19")]
    public readonly InputSlot<bool> Paused = new();

    [Input(Guid = "47787d21-9ddc-45ab-97f6-a573a717675c")]
    public readonly InputSlot<int> MaxContacts = new(64);

    [Input(Guid = "142c8ba1-4d45-4410-a4cf-bdf08b256717")]
    public readonly InputSlot<float> ContactSpeed = new(1f);
}
