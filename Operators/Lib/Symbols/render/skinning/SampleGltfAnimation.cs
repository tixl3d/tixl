#nullable enable
using T3.Core.Utils;

namespace Lib.render.skinning;

/// <summary>
/// Samples an animation clip of a loaded glTF scene into a pose: one point per joint
/// with joint-local position, orientation and scale, ready for [PoseToSkinMatrices].
/// </summary>
[Guid("e58d1c9a-4b26-4f83-a071-c2d95b3f68e4")]
internal sealed class SampleGltfAnimation : Instance<SampleGltfAnimation>, IStatusProvider
{
    [Output(Guid = "6C30F7D2-98E5-4A1B-BD47-05A8C1E29F63", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Point[]?> Pose = new();

    public SampleGltfAnimation()
    {
        Pose.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var setup = Setup.GetValue(context);
        var clipIndexInput = ClipIndex.GetValue(context);
        var skeletonIndexInput = SkeletonIndex.GetValue(context);
        var loop = Loop.GetValue(context);

        // Clip times are in seconds; context time is in bars
        var timeInSecs = OverrideTime.HasInputConnections
                             ? OverrideTime.GetValue(context)
                             : (float)(context.LocalFxTime * 240 / context.Playback.Bpm);

        if (setup == null || setup.Skeletons.Count == 0)
        {
            _lastErrorMessage = "Scene has no skeletons. Connect the setup of a rigged glTF model.";
            Pose.Value = null;
            return;
        }

        if (setup.AnimationClips.Count == 0)
        {
            _lastErrorMessage = "Scene has no animation clips";
            Pose.Value = null;
            return;
        }

        var skeletonIndex = skeletonIndexInput.Mod(setup.Skeletons.Count);
        var skeleton = setup.Skeletons[skeletonIndex];
        var clip = setup.AnimationClips[clipIndexInput.Mod(setup.AnimationClips.Count)];

        var jointCount = skeleton.ParentIndices.Length;
        if (_pose.Length != jointCount)
        {
            _pose = new Point[jointCount];
        }

        // Joints without animation channels keep their rest transform
        for (var jointIndex = 0; jointIndex < jointCount; jointIndex++)
        {
            var rest = skeleton.RestLocalTransforms[jointIndex];
            _pose[jointIndex] = new Point
                                    {
                                        Position = rest.Translation,
                                        Orientation = rest.Rotation,
                                        Scale = rest.Scale,
                                        Color = Vector4.One,
                                        F1 = 1,
                                        F2 = skeleton.ParentIndices[jointIndex],
                                    };
        }

        var sampleTime = timeInSecs;
        if (clip.Duration > 0)
        {
            if (loop)
            {
                sampleTime %= clip.Duration;
                if (sampleTime < 0)
                {
                    sampleTime += clip.Duration;
                }
            }
            else
            {
                sampleTime = sampleTime.Clamp(0, clip.Duration);
            }
        }

        for (var channelIndex = 0; channelIndex < clip.Channels.Count; channelIndex++)
        {
            var channel = clip.Channels[channelIndex];
            if (channel.SkeletonIndex != skeletonIndex)
                continue;

            var jointIndex = channel.JointIndex;
            if (jointIndex < 0 || jointIndex >= jointCount)
                continue;

            if (channel.TranslationSampler != null)
            {
                _pose[jointIndex].Position = channel.TranslationSampler.GetPoint(sampleTime);
            }

            if (channel.RotationSampler != null)
            {
                _pose[jointIndex].Orientation = Quaternion.Normalize(channel.RotationSampler.GetPoint(sampleTime));
            }

            if (channel.ScaleSampler != null)
            {
                _pose[jointIndex].Scale = channel.ScaleSampler.GetPoint(sampleTime);
            }
        }

        Pose.Value = _pose;
    }

    #region status provider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return _lastErrorMessage == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage ?? string.Empty;
    }
    #endregion

    private Point[] _pose = [];
    private string? _lastErrorMessage;

    [Input(Guid = "2a95cd41-7e68-4c0f-9b83-d15f0a62e837")]
    public readonly InputSlot<SceneSetup> Setup = new();

    [Input(Guid = "84b0f3e6-1d59-4c27-a9ce-72e8d4b1065f")]
    public readonly InputSlot<int> ClipIndex = new();

    [Input(Guid = "f6e21b48-935a-4d70-8c1e-40a7d92c53b1")]
    public readonly InputSlot<int> SkeletonIndex = new();

    [Input(Guid = "07d54c92-eab3-4861-b1f5-c8394e60da27")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "b3861f05-4dc2-49e8-92a7-6ff0e1d8c534")]
    public readonly InputSlot<bool> Loop = new();
}
